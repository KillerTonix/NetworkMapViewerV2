param(
	[string]$IP,
	[string]$Password
)
$modulePath = "C:\Program Files\WindowsPowerShell\Modules\PSTerminalServices\1.0\PSTerminalServices.psd1"

if (Test-Path $modulePath) {
    Import-Module $modulePath
} else {
	$wshell = New-Object -ComObject Wscript.Shell
	$wshell.Popup("PSTerminalServices module not found.`nCopy and Paste the following command to install this package.`nInstall-Module -Name PSTerminalServices", 0, "Module not found.", 0)
    exit
}

$Username = 'administrator'
$pass = ConvertTo-SecureString -AsPlainText $Password -Force
$cred = New-Object System.Management.Automation.PSCredential ($Username, $pass)

if (Test-Connection -ComputerName $IP -Count 1 -Quiet) {   
    try {
        if (!(Get-Module -ListAvailable Posh-SSH)) {
            $wshell = New-Object -ComObject Wscript.Shell
			$wshell.Popup("Posh-SSH module not found.`nCopy and Paste the following command to install this package.`nInstall-Module Posh-SSH", 0, "Module not found.", 0)
            exit
        }

        $session = New-SSHSession -ComputerName $IP -Credential $cred -AcceptKey -ErrorAction Stop

        if ($session.Connected) {
            
            # Run bash commands with a strict 250ms cooldown to prevent OpenSSH rate-limiting
            $nameOut = Invoke-SSHCommand -SSHSession $session -Command 'hostname'
            Start-Sleep -Milliseconds 250

            $macOut  = Invoke-SSHCommand -SSHSession $session -Command 'ip link | awk ''/ether/ {print $2; exit}'''
            Start-Sleep -Milliseconds 250

            $cpuOut  = Invoke-SSHCommand -SSHSession $session -Command 'grep -m1 "model name" /proc/cpuinfo | cut -d: -f2'
            Start-Sleep -Milliseconds 250

            $ramOut  = Invoke-SSHCommand -SSHSession $session -Command 'awk ''/MemTotal/ {printf "%d", ($2/1024/1024)==int($2/1024/1024) ? ($2/1024/1024) : int(($2/1024/1024)+1)}'' /proc/meminfo'
            Start-Sleep -Milliseconds 250

            $osOut   = Invoke-SSHCommand -SSHSession $session -Command 'lsb_release -d | cut -f2-'

            # Clean up the returned strings
            $name = $nameOut.Output.Trim()
            $mac = $macOut.Output.Trim()
            $rawCpu = $cpuOut.Output.Trim()
            $ram = $ramOut.Output.Trim() + "GB"
            $os = $osOut.Output.Trim()

            if ($rawCpu -match '(i\d-[a-zA-Z0-9]+)') {
                $cleanCpu = $matches[1] 
            } elseif ($rawCpu -match '(Ryzen \d [a-zA-Z0-9]+)') {
                $cleanCpu = $matches[1] 
            } else {
                $cleanCpu = ($rawCpu -replace ' @.*', '') -replace ' CPU', ''
                $cleanCpu = $cleanCpu.Replace("Intel(R) ", "").Replace("(R)", "").Replace("(TM)", "")
            }

            # =================================================================
            # CACHED TERMINAL SERVER SESSION LOOKUP (10 MINUTE LIFESPAN)
            # =================================================================
            $cacheFile = "$env:TEMP\NMV_TSSessions_Cache.json"
            $lockFile = "$env:TEMP\NMV_TSSessions_Cache.lock"
            $cacheAgeLimit = (Get-Date).AddMinutes(-10)

            function Read-Cache {
                if ((Test-Path $cacheFile) -and ((Get-Item $cacheFile).LastWriteTime -gt $cacheAgeLimit)) {
                    return (Get-Content $cacheFile -Raw | ConvertFrom-Json)
                }
                return $null
            }

            $cachedData = Read-Cache

            # If cache is missing or expired, rebuild it
            if ($null -eq $cachedData) {
                
                # THE FIX: STALE LOCK DELETER
                # If a script crashed previously and left a lock file older than 3 minutes, destroy it.
                if ((Test-Path $lockFile) -and ((Get-Item $lockFile).LastWriteTime -lt (Get-Date).AddMinutes(-3))) {
                    Remove-Item $lockFile -Force -ErrorAction SilentlyContinue
                }

                $lockAcquired = $false
                for ($i = 0; $i -lt 30; $i++) {
                    try {
                        $fileStream = [System.IO.File]::Open($lockFile, 'CreateNew', 'Write', 'None')
                        $lockAcquired = $true
                        break
                    } catch {
                        Start-Sleep -Seconds 1
                    }
                }

                if ($lockAcquired) {
                    try {
                        $cachedData = Read-Cache
                        if ($null -eq $cachedData) {
                            $terminalServers = "rd0", "rd01", "rd1", "rd02", "rd03", "rd04", "rd05", "rd06", "rd07", "rd08", "rd09", "rd10"
                            $sessionMap = @{}
                            
                            foreach ($server in $terminalServers) {
                                $tsSessions = Get-TSSession -ComputerName $server -State Active -ErrorAction SilentlyContinue | Select-Object IPAddress, UserName
                                if ($tsSessions) {
                                    foreach ($s in $tsSessions) {
                                        if ($s.IPAddress -and $s.UserName) {
                                            # THE FIX: Strictly cast to String so JSON serialization doesn't crash on COM objects
                                            $sessionMap[$s.IPAddress.ToString()] = $s.UserName.ToString()
                                        }
                                    }
                                }
                            }
                            
                            # THE FIX: Safely convert the hashtable using -InputObject
                            $jsonOutput = ConvertTo-Json -InputObject $sessionMap -Compress
                            Set-Content -Path $cacheFile -Value $jsonOutput -Force
                            
                            $cachedData = Read-Cache
                        }
                    } catch {
                        # NEW: If it crashes while building the map, drop an error log so we know exactly why!
                        Set-Content -Path "$env:TEMP\NMV_CacheError.txt" -Value $_.Exception.Message -Force
                    } finally {
                        if ($null -ne $fileStream) {
                            $fileStream.Close()
                        }
                        Remove-Item $lockFile -Force -ErrorAction SilentlyContinue
                    }
                } else {
                    $cachedData = Read-Cache
                }
            }

            # Extract the user from the fast JSON Cache
            $userCn = ''
            if ($cachedData -and $cachedData.PSObject.Properties.Name -contains $IP) {
                $sessionT = $cachedData."$IP"
                $adUser = Get-ADUser -Identity $sessionT -Properties cn -ErrorAction SilentlyContinue
                if ($adUser) {
                    $userCn = $adUser.cn
                }
            }
            # =================================================================

            # OUTPUT RAW DATA FOR C# PARSER
            Write-Output "NAME=$name"
            Write-Output "MAC=$mac"
            Write-Output "CPU=$cleanCpu"
            Write-Output "RAM=$ram"
            Write-Output "OS=$os"
            Write-Output "USERNAME=$userCn"

            # Always close the session
            Remove-SSHSession -SSHSession $session | Out-Null
        } else {
            $wshell = New-Object -ComObject Wscript.Shell
			$wshell.Popup("ERROR=SSH Connection Refused.",0,"Error Connection",0)
        }
    } catch {
		Write-Output "ERROR=$($_.Exception.Message)"
        $wshell = New-Object -ComObject Wscript.Shell
		$wshell.Popup("$($_.Exception.Message)",0,"Error",0)
    }
} else {
	Write-Output "ERROR=Device is offline or ping is blocked."
    $wshell = New-Object -ComObject Wscript.Shell
	$wshell.Popup("Device is offline or ping is blocked.",0,"Error",0)
}