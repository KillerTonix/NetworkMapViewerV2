$Date = (Get-Date).AddDays(-15)

$computers = Get-ADComputer -Filter 'whenCreated -ge $Date' -SearchBase "DC=evoca,DC=am" -Properties whenCreated | ForEach-Object {
    $ip = try {
        ([System.Net.Dns]::GetHostAddresses($_.Name) | 
            Where-Object { $_.AddressFamily -eq 'InterNetwork' } | 
            Select-Object -First 1).IPAddressToString
    } catch {
        "Not Found"
    }

    [PSCustomObject]@{
        Name        = $_.Name
        IPAddress   = if ($ip) { $ip } else { "Not Found" }
        WhenCreated = if ($_.whenCreated) { $_.whenCreated.ToString("o") } else { $null }
    }
}

# Return JSON output for C#
if ($computers) {
    $computers | ConvertTo-Json -Compress
}