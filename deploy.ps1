param(
    [ValidateSet("portal", "www", "cloud")]
    [Parameter(Mandatory = $true)]
    [string]$AppName
)

# 1. Create new git repository
git init -b main

# 2. Commit changes
git add .
git commit -m "Deploy $AppName"

# 3. Deploy with CapRover
caprover deploy -n xvirus -a $AppName -b main

# 4. Delete the new git repository
Remove-Item -Recurse -Force .git
