param(
    [ValidateSet("Portal", "Website", "Cloud")]
    [Parameter(Mandatory = $true)]
    [string]$Target
)

# Map folder names to CapRover app names
$AppNameMap = @{
    "Portal"  = "portal"
    "Website" = "www"
    "Cloud"   = "cloud"
}
$AppName = $AppNameMap[$Target]

# Generate a unique branch name
$BranchName = "deploy-$($Target.ToLower())-$(Get-Date -Format 'yyyyMMddHHmmss')"

# 1. Create new branch
git checkout -b $BranchName

# 2. Delete everything except 'target' and '.git'
Get-ChildItem -Force | Where-Object {
    $_.Name -ne $Target -and $_.Name -ne '.git'
} | Remove-Item -Recurse -Force

# 3. Move target contents to root
Move-Item -Path $Target\* -Destination .

# 4. Remove empty folder
Remove-Item $Target

# 5. Commit changes
git add .
git commit -m "Deploy $Target"

# 6. Deploy with CapRover
caprover deploy -n xvirus -a $AppName -b $BranchName

# 7. Return to previous branch
$previous = git rev-parse --abbrev-ref @{-1}
git checkout $previous

# 8. Delete the temporary branch
git branch -D $BranchName
