[CmdletBinding()]
param(
    [string]$Root
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = Join-Path $PSScriptRoot "..\.."
}

$rootPath = [IO.Path]::GetFullPath($Root)
$engineBin = Join-Path $rootPath "engine\bin"
$luaLibrary = Join-Path $engineBin "lua51.dll"
$mapsPath = Join-Path $rootPath "mods\sa\maps"

if (!(Test-Path -LiteralPath $luaLibrary -PathType Leaf))
{
    throw "Bundled Lua runtime not found: $luaLibrary. Build the engine first."
}

if (!("OpenSALua51Native" -as [type]))
{
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class OpenSALua51Native
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetDllDirectory(string path);

    [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr luaL_newstate();

    [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int luaL_loadfile(IntPtr state, string fileName);

    [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr lua_tolstring(IntPtr state, int index, out UIntPtr length);

    [DllImport("lua51.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void lua_close(IntPtr state);
}
"@
}

if (![OpenSALua51Native]::SetDllDirectory($engineBin))
{
    throw "Unable to configure the bundled Lua runtime search path: $engineBin"
}

$scripts = @(Get-ChildItem -LiteralPath $mapsPath -Filter "*.lua" -File -Recurse | Sort-Object FullName)
$failures = New-Object System.Collections.Generic.List[string]
foreach ($script in $scripts)
{
    $state = [OpenSALua51Native]::luaL_newstate()
    if ($state -eq [IntPtr]::Zero)
    {
        throw "Unable to create a Lua parser state."
    }

    try
    {
        $result = [OpenSALua51Native]::luaL_loadfile($state, $script.FullName)
        if ($result -ne 0)
        {
            [UIntPtr]$length = [UIntPtr]::Zero
            $messagePointer = [OpenSALua51Native]::lua_tolstring($state, -1, [ref]$length)
            if ($messagePointer -eq [IntPtr]::Zero)
            {
                $message = "Unknown Lua parser error."
            }
            else
            {
                $message = [Runtime.InteropServices.Marshal]::PtrToStringAnsi($messagePointer)
            }

            $failures.Add("$($script.FullName): $message")
        }
    }
    finally
    {
        [OpenSALua51Native]::lua_close($state)
    }
}

if ($failures.Count -gt 0)
{
    $failures | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    Write-Host "Lua syntax validation failed for $($failures.Count) of $($scripts.Count) scripts." -ForegroundColor Red
    exit 1
}

Write-Host "Lua syntax validation passed for $($scripts.Count) scripts." -ForegroundColor Green
exit 0
