[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $AssemblyDirectory,

    [string] $BaselinePath = (Join-Path $PSScriptRoot 'public-api-baseline.txt'),

    [switch] $UpdateBaseline
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Format-TypeName {
    param([Parameter(Mandatory = $true)][Type] $Type)

    if ($Type.IsByRef) {
        return (Format-TypeName $Type.GetElementType()) + '&'
    }
    if ($Type.IsPointer) {
        return (Format-TypeName $Type.GetElementType()) + '*'
    }
    if ($Type.IsArray) {
        return (Format-TypeName $Type.GetElementType()) + '[' + (',' * ($Type.GetArrayRank() - 1)) + ']'
    }
    if ($Type.IsGenericParameter) {
        return $Type.Name
    }
    if ($Type.IsGenericType) {
        $name = $Type.GetGenericTypeDefinition().FullName
        $name = $name.Substring(0, $name.IndexOf('`'))
        $arguments = @($Type.GetGenericArguments() | ForEach-Object { Format-TypeName $_ })
        return $name + '<' + ($arguments -join ',') + '>'
    }
    if ($Type.FullName) {
        return $Type.FullName
    }
    return $Type.Name
}

function Format-Parameter {
    param([Parameter(Mandatory = $true)][System.Reflection.ParameterInfo] $Parameter)

    $modifiers = @()
    $parameterType = $Parameter.ParameterType
    if ($Parameter.GetCustomAttributes([ParamArrayAttribute], $false).Count -gt 0) {
        $modifiers += 'params'
    }
    if ($Parameter.IsOut) {
        $modifiers += 'out'
    }
    elseif ($parameterType.IsByRef) {
        $isReadOnly = $Parameter.IsIn -or @(
            $Parameter.GetRequiredCustomModifiers() |
                Where-Object { $_.FullName -eq 'System.Runtime.CompilerServices.IsReadOnlyAttribute' }
        ).Count -gt 0
        $modifiers += if ($isReadOnly) { 'in' } else { 'ref' }
    }
    if ($parameterType.IsByRef) {
        $parameterType = $parameterType.GetElementType()
    }
    $optional = ''
    if ($Parameter.IsOptional) {
        $defaultValue = if ($Parameter.HasDefaultValue -and $null -ne $Parameter.DefaultValue) {
            [Convert]::ToString(
                $Parameter.DefaultValue,
                [System.Globalization.CultureInfo]::InvariantCulture)
        } else { '<null>' }
        $optional = " optional=$defaultValue"
    }
    $prefix = if ($modifiers.Count -gt 0) { ($modifiers -join ' ') + ' ' } else { '' }
    $name = if ($Parameter.Name) { $Parameter.Name } else { '<unnamed>' }
    return $prefix + (Format-TypeName $parameterType) + ' ' + $name + $optional
}

function Get-AccessorFlags {
    param([Parameter(Mandatory = $true)][System.Reflection.MethodInfo[]] $Accessors)

    $flags = @()
    if (@($Accessors | Where-Object { $_.IsStatic }).Count -gt 0) {
        $flags += 'static'
    }
    if (@($Accessors | Where-Object { $_.IsAbstract }).Count -gt 0) {
        $flags += 'abstract'
    }
    elseif (@($Accessors | Where-Object { $_.IsVirtual -and -not $_.IsFinal }).Count -gt 0) {
        $flags += 'virtual'
    }
    return $flags
}

function Get-TypeKind {
    param([Parameter(Mandatory = $true)][Type] $Type)

    if ($Type.IsInterface) { return 'interface' }
    if ($Type.IsEnum) { return 'enum' }
    if ($Type.BaseType -eq [System.MulticastDelegate]) { return 'delegate' }
    if ($Type.IsValueType) { return 'struct' }
    return 'class'
}

function Get-PublicApiLines {
    param([Parameter(Mandatory = $true)][System.Reflection.Assembly[]] $Assemblies)

    $lines = [System.Collections.Generic.List[string]]::new()
    $flags = [System.Reflection.BindingFlags]'Public,Instance,Static,DeclaredOnly'

    foreach ($assembly in $Assemblies) {
        $assemblyName = $assembly.GetName().Name
        foreach ($type in @($assembly.GetExportedTypes())) {
            $typeName = Format-TypeName $type
            $modifiers = @()
            if ($type.IsAbstract) { $modifiers += 'abstract' }
            if ($type.IsSealed) { $modifiers += 'sealed' }
            $baseType = if ($type.BaseType) { Format-TypeName $type.BaseType } else { '-' }
            $interfaces = @($type.GetInterfaces() | ForEach-Object { Format-TypeName $_ } | Sort-Object)
            $underlyingType = if ($type.IsEnum) {
                Format-TypeName ([Enum]::GetUnderlyingType($type))
            } else { '-' }
            $lines.Add(
                "T|$assemblyName|$typeName|$(Get-TypeKind $type)|$($modifiers -join ',')|base=$baseType|interfaces=$($interfaces -join ',')|underlying=$underlyingType")

            foreach ($constructor in $type.GetConstructors($flags)) {
                $parameters = @($constructor.GetParameters() | ForEach-Object { Format-Parameter $_ })
                $lines.Add("C|$assemblyName|$typeName|($($parameters -join ','))")
            }

            foreach ($field in $type.GetFields($flags)) {
                $fieldFlags = @()
                if ($field.IsStatic) { $fieldFlags += 'static' }
                if ($field.IsInitOnly) { $fieldFlags += 'readonly' }
                if ($field.IsLiteral) { $fieldFlags += 'const' }
                $constantValue = if ($field.IsLiteral) {
                    'value=' + [Convert]::ToString(
                        $field.GetRawConstantValue(),
                        [System.Globalization.CultureInfo]::InvariantCulture)
                } else { '' }
                $lines.Add(
                    "F|$assemblyName|$typeName|$($field.Name)|$(Format-TypeName $field.FieldType)|$($fieldFlags -join ',')|$constantValue")
            }

            foreach ($property in $type.GetProperties($flags)) {
                $accessors = @($property.GetAccessors($false))
                $access = @()
                if ($accessors.Name -contains ('get_' + $property.Name)) { $access += 'get' }
                if ($accessors.Name -contains ('set_' + $property.Name)) { $access += 'set' }
                $propertyFlags = @(Get-AccessorFlags $accessors)
                $indexParameters = @($property.GetIndexParameters() | ForEach-Object { Format-Parameter $_ })
                $lines.Add(
                    "P|$assemblyName|$typeName|$($property.Name)|$(Format-TypeName $property.PropertyType)|index=$($indexParameters -join ',')|$($access -join ',')|$($propertyFlags -join ',')")
            }

            foreach ($eventInfo in $type.GetEvents($flags)) {
                $allEventAccessors = @(
                    $eventInfo.GetAddMethod($false),
                    $eventInfo.GetRemoveMethod($false),
                    $eventInfo.GetRaiseMethod($false)
                )
                $eventAccessors = @($allEventAccessors | Where-Object { $null -ne $_ })
                $eventFlags = @(Get-AccessorFlags $eventAccessors)
                $lines.Add(
                    "E|$assemblyName|$typeName|$($eventInfo.Name)|$(Format-TypeName $eventInfo.EventHandlerType)|$($eventFlags -join ',')")
            }

            foreach ($method in $type.GetMethods($flags)) {
                if ($method.IsSpecialName -and $method.Name -match '^(get_|set_|add_|remove_)') {
                    continue
                }
                $methodFlags = @()
                if ($method.IsStatic) { $methodFlags += 'static' }
                if ($method.IsAbstract) { $methodFlags += 'abstract' }
                elseif ($method.IsVirtual -and -not $method.IsFinal) { $methodFlags += 'virtual' }
                $genericArguments = @($method.GetGenericArguments() | ForEach-Object { $_.Name })
                $genericSuffix = if ($genericArguments.Count -gt 0) {
                    '<' + ($genericArguments -join ',') + '>'
                } else { '' }
                $parameters = @($method.GetParameters() | ForEach-Object { Format-Parameter $_ })
                $lines.Add(
                    "M|$assemblyName|$typeName|$($method.Name)$genericSuffix|$(Format-TypeName $method.ReturnType)|($($parameters -join ','))|$($methodFlags -join ',')")
            }
        }
    }

    $array = $lines.ToArray()
    [Array]::Sort($array, [StringComparer]::Ordinal)
    return $array
}

function Load-ManagedAssembly {
    param([Parameter(Mandatory = $true)][string] $Path)

    $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($Path)
    $loaded = [AppDomain]::CurrentDomain.GetAssemblies() |
        Where-Object { $_.GetName().Name -eq $assemblyName.Name } |
        Select-Object -First 1
    if ($loaded) {
        return $loaded
    }
    return [System.Runtime.Loader.AssemblyLoadContext]::Default.LoadFromAssemblyPath($Path)
}

$directory = (Resolve-Path -LiteralPath $AssemblyDirectory).Path
foreach ($dependency in @('OpenCvSharp.dll', 'OpenCvSharp.Blob.dll')) {
    $dependencyPath = Join-Path $directory $dependency
    if (Test-Path -LiteralPath $dependencyPath) {
        $null = Load-ManagedAssembly $dependencyPath
    }
}

$assemblyNames = @(
    'OpenVisionLab.Core',
    'OpenVisionLab.Inspection',
    'OpenVisionLab.Vision2D',
    'OpenVisionLab.Vision2D.Blob',
    'OpenVisionLab.Vision3D'
)
$assemblies = foreach ($name in $assemblyNames) {
    $path = Join-Path $directory ($name + '.dll')
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing public API assembly: $path"
    }
    Load-ManagedAssembly $path
}
$current = @(Get-PublicApiLines $assemblies)

if ($UpdateBaseline) {
    $header = @(
        '# OpenVisionLab public API compatibility baseline',
        '# Generated by eng/Verify-PublicApi.ps1. Any difference requires an explicit compatibility decision and baseline update.'
    )
    [System.IO.File]::WriteAllLines(
        [System.IO.Path]::GetFullPath($BaselinePath),
        @($header + $current),
        [System.Text.UTF8Encoding]::new($false))
    Write-Host "Public API baseline updated: $BaselinePath ($($current.Count) entries)"
    return
}

$baselineFile = (Resolve-Path -LiteralPath $BaselinePath).Path
$baseline = @(Get-Content -LiteralPath $baselineFile | Where-Object { $_ -and -not $_.StartsWith('#') })
$currentSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in $current) {
    $null = $currentSet.Add($entry)
}
$baselineSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in $baseline) {
    $null = $baselineSet.Add($entry)
}
$missing = @($baseline | Where-Object { -not $currentSet.Contains($_) })
$added = @($current | Where-Object { -not $baselineSet.Contains($_) })
if ($missing.Count -gt 0 -or $added.Count -gt 0) {
    $details = @()
    if ($missing.Count -gt 0) {
        $details += "Missing or changed entries:`n$($missing -join [Environment]::NewLine)"
    }
    if ($added.Count -gt 0) {
        $details += "Added entries require an explicit baseline update:`n$($added -join [Environment]::NewLine)"
    }
    throw "Public API compatibility failed.`n$($details -join [Environment]::NewLine)"
}

Write-Host "Public API compatibility passed exactly: $($baseline.Count) baseline entries."
