param(
    [ValidateSet('prod','debug','uv','alpha','color','cycle')]
    [string]$Mode = 'cycle',

    [switch]$DebugLayer,

    [string]$Configuration = 'Debug',

    [string]$Framework = 'net10.0-windows'
)

$ErrorActionPreference = 'Stop'

$solutionRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $solutionRoot 'Dx12WinForm\Dx12WinForm.csproj'

function Start-App([string]$ps)
{
    Write-Host "Launching with IMGUI_DX12_PS=$ps" -ForegroundColor Cyan

    $env:IMGUI_DX12_PS = $ps
    if ($DebugLayer)
    {
        $env:DX12_FORCE_DEBUG_LAYER = '1'
    }
    else
    {
        Remove-Item Env:\DX12_FORCE_DEBUG_LAYER -ErrorAction SilentlyContinue
    }

    dotnet run --project $projectPath -c $Configuration -f $Framework
}

switch ($Mode)
{
    'cycle'
    {
        foreach ($ps in @('debug','uv','alpha','color','prod'))
        {
            Start-App $ps

            Write-Host ''
            Write-Host 'Close the window to continue to the next variant...' -ForegroundColor DarkGray
            Write-Host ''
        }
    }
    default
    {
        Start-App $Mode
    }
}
