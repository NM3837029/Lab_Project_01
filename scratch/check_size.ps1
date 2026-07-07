Add-Type -AssemblyName System.Drawing
$img1 = [System.Drawing.Image]::FromFile('img/jimen.png')
$img2 = [System.Drawing.Image]::FromFile('img/tuti.png')
Write-Output "jimen: Width=$($img1.Width), Height=$($img1.Height)"
Write-Output "tuti: Width=$($img2.Width), Height=$($img2.Height)"
