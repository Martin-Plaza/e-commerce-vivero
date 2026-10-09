[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Cuit,

    [Parameter(Mandatory = $true)]
    [string]$Organization,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$CommonName = "GymShopHomologacion"
)

$digits = $Cuit -replace '\D', ''
if ($digits -notmatch '^\d{11}$') {
    throw 'Cuit must contain exactly 11 digits.'
}

$target = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($target) | Out-Null
$keyPath = [System.IO.Path]::Combine($target, 'arca-homologation-private-key.pem')
$csrPath = [System.IO.Path]::Combine($target, 'arca-homologation-request.csr')
if ([System.IO.File]::Exists($keyPath) -or [System.IO.File]::Exists($csrPath)) {
    throw "The target already contains ARCA key or CSR files: $target"
}

$escapedOrganization = $Organization.Replace(',', '\,').Trim()
$escapedCommonName = $CommonName.Replace(',', '\,').Trim()
$subject = "C=AR, O=$escapedOrganization, CN=$escapedCommonName, SERIALNUMBER=CUIT $digits"
$rsa = [System.Security.Cryptography.RSA]::Create(2048)
try {
    $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        $subject,
        $rsa,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)

    [System.IO.File]::WriteAllText($keyPath, $rsa.ExportPkcs8PrivateKeyPem())
    [System.IO.File]::WriteAllText($csrPath, $request.CreateSigningRequestPem())
}
finally {
    $rsa.Dispose()
}

Write-Output "CSR: $csrPath"
Write-Output "Private key: $keyPath"
Write-Warning 'Keep the private key secret. Upload only the CSR to WSASS and never commit either file.'
