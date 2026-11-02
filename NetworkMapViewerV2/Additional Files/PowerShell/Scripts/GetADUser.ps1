param($Username)

try {	
	$user = Get-ADUser -Identity $Username -Properties cn
	Write-Output "USERNAME=$($user.cn)"	
} catch {
	Write-Output "ERROR=$($_.Exception.Message)"
	$wshell = New-Object -ComObject Wscript.Shell
	$wshell.Popup("$($_.Exception.Message)",0,"Error",0)
}
