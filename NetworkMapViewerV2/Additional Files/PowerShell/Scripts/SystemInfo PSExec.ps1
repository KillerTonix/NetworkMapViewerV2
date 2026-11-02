try {
    # Notice: No -ComputerName or -Credential! It queries the local machine natively.
    $sys = Get-WmiObject -Class Win32_ComputerSystem -ErrorAction Stop
    $os  = Get-WmiObject -Class Win32_OperatingSystem
    $cpu = Get-WmiObject -Class Win32_Processor | Select-Object -First 1
    $gpu = Get-WmiObject -Class Win32_VideoController | Select-Object -First 1
    
    $ram = Get-WmiObject -Class Win32_PhysicalMemory
    $totalRam = 0
    foreach ($stick in $ram) { $totalRam += $stick.Capacity }
    $ramGb = [math]::Round($totalRam / 1GB, 0)

    $disk = Get-WmiObject -Class Win32_DiskDrive | Select-Object -First 1
    $diskGb = [math]::Round($disk.Size / 1GB, 0)

    $net = Get-WmiObject -Class Win32_NetworkAdapterConfiguration -Filter "IPEnabled=True" | Select-Object -First 1

    $cleanCpu = $cpu.Name
    if ($cleanCpu -match '(i\d-[a-zA-Z0-9]+)') {
        $cleanCpu = $matches[1] 
    } elseif ($cleanCpu -match '(Ryzen \d [a-zA-Z0-9]+)') {
        $cleanCpu = $matches[1] 
    } else {
        $cleanCpu = ($cleanCpu -replace ' @.*', '') -replace ' CPU', ''
        $cleanCpu = $cleanCpu.Replace("Intel(R) ", "").Replace("(R)", "").Replace("(TM)", "")
    }	
	# 1. Run the command and store the text output
	$quserOutput = quser.exe

	# 2. Grab the second line (index 1 skips the header), and trim leading spaces
	$userLine = $quserOutput[1].Trim()

	# 3. Split the line by spaces and grab the very first item (the username)
	$username = ($userLine -split '\s+')[0]

	# 4. Clean up the output (quser sometimes adds a '>' before the active user)
	$username = $username.Replace('>', '')


    Write-Output "NAME=$($sys.Name)"
    Write-Output "MAC=$($net.MACAddress)"
    Write-Output "CPU=$cleanCpu"
    Write-Output "RAM=$($ramGb)GB"
    Write-Output "SSD=$($disk.Model) ($($diskGb)GB)"
    Write-Output "GRAPHIC=$($gpu.Name)"
    Write-Output "OS=$($os.Caption)"
	Write-Output "USERNAME=$username"

} catch {
	Write-Output "ERROR=$($_.Exception.Message)"
	$wshell = New-Object -ComObject Wscript.Shell
	$wshell.Popup("$($_.Exception.Message)",0,"Error",0)
}
