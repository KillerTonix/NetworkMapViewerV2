param(
	[string]$IP,
	[string]$Password1,	
	[string]$Password2
)

$modulePath = "C:\Program Files\WindowsPowerShell\Modules\PSTerminalServices\1.0\PSTerminalServices.psd1"

if (Test-Path $modulePath) {
    Import-Module $modulePath
} else {
	$wshell = New-Object -ComObject Wscript.Shell
	$wshell.Popup("PSTerminalServices module not found.`nCopy and Paste the following command to install this package.`nInstall-Module -Name PSTerminalServices", 0, "Module not found.", 0)
    exit
}

$terminalServers = "rd0", "rd01", "rd1", "rd02", "rd03", "rd04", "rd05", "rd06", "rd07", "rd08", "rd09", "rd10"
$Username1 = 'evadmin'
$Username2 = 'administrator'
$pass1 = ConvertTo-SecureString -AsPlainText $Password1 -Force
$pass2 = ConvertTo-SecureString -AsPlainText $Password2 -Force

# Users you password securly
$cred = New-Object -TypeName System.Management.Automation.PSCredential -ArgumentList $Username1,$pass1 

if (Test-Connection -ComputerName $IP -Count 1 -Quiet) {

	# Check Accessability if get UnauthorizedAccessException then use second login
	try {
		$system = Get-WmiObject -Class Win32_ComputerSystem -ComputerName $IP -Credential $cred -ErrorAction Stop  
	} catch [System.UnauthorizedAccessException] {
		$cred = New-Object -TypeName System.Management.Automation.PSCredential -ArgumentList $Username2,$pass1 
	}
	# Check Accessability if get UnauthorizedAccessException then use second password
	try {
		$system = Get-WmiObject -Class Win32_ComputerSystem -ComputerName $IP -Credential $cred -ErrorAction Stop  
	} catch [System.UnauthorizedAccessException] {
		$cred = New-Object -TypeName System.Management.Automation.PSCredential -ArgumentList $Username2,$pass2 
	}
	
    try {
        # Using classic WMI with Credentials
        $sys = Get-WmiObject -Class Win32_ComputerSystem -ComputerName $IP -Credential $cred -ErrorAction Stop
        $os  = Get-WmiObject -Class Win32_OperatingSystem -ComputerName $IP -Credential $cred
        $cpu = Get-WmiObject -Class Win32_Processor -ComputerName $IP -Credential $cred | Select-Object -First 1
        $gpu = Get-WmiObject -Class Win32_VideoController -ComputerName $IP -Credential $cred | Select-Object -First 1
        
		# --- CLEAN UP CPU STRING ---
        $cleanCpu = $cpu.Name
        if ($cleanCpu -match '(i\d-[a-zA-Z0-9]+)') {
            $cleanCpu = $matches[1] # Extracts exactly "i5-11400"
        } elseif ($cleanCpu -match '(Ryzen \d [a-zA-Z0-9]+)') {
            $cleanCpu = $matches[1] # Extracts "Ryzen 5 5600X" (Just in case you have AMD!)
        } else {
            # Fallback for older/other CPUs: just cut off the "@ 2.90GHz" and "CPU" text
            $cleanCpu = ($cleanCpu -replace ' @.*', '') -replace ' CPU', ''
        }
		
        # Calculate Total RAM
        $ram = Get-WmiObject -Class Win32_PhysicalMemory -ComputerName $IP -Credential $cred
        $totalRam = 0
        foreach ($stick in $ram) { $totalRam += $stick.Capacity }
        $ramGb = [math]::Round($totalRam / 1GB, 0)

        # Get Primary Storage Drive
        $disk = Get-WmiObject -Class Win32_DiskDrive -ComputerName $IP -Credential $cred | Select-Object -First 1
        $diskGb = [math]::Round($disk.Size / 1GB, 0)

        # Get Active Network Adapter MAC
        $net = Get-WmiObject -Class Win32_NetworkAdapterConfiguration -Filter "IPEnabled=True" -ComputerName $IP -Credential $cred | Select-Object -First 1
		$user = ''
		foreach ($server in $terminalServers){
			$outputTSSession = Get-TSSession -ComputerName $server -State Active | select IPAddress, UserName

			if ($outputTSSession.IPAddress -contains $IP) {
				$session = ($outputTSSession | Where-Object { $_.IPAddress -eq $IP }).UserName				
				$user = Get-ADUser -Identity $session -Properties cn
				break
			}
		}
	
        # OUTPUT RAW DATA FOR C# PARSER
        Write-Output "NAME=$($sys.Name)"
        Write-Output "MAC=$($net.MACAddress)"
        Write-Output "CPU=$cleanCpu"
        Write-Output "RAM=$($ramGb)GB"
        Write-Output "SSD=$($disk.Model) ($($diskGb)GB)"
        Write-Output "GRAPHIC=$($gpu.Name)"
        Write-Output "OS=$($os.Caption)"
		Write-Output "USERNAME=$($user.cn)"

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