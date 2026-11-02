<# Reproducible numeric summary for profiler sample CSVs. No missing samples are invented. #>
param(
    [Parameter(Mandatory=$true)][string]$InputCsv,
    [Parameter(Mandatory=$true)][string[]]$MetricColumns,
    [string[]]$GroupColumns=@(),
    [string]$OutputCsv=''
)
$ErrorActionPreference='Stop'
$MetricColumns=@($MetricColumns|ForEach-Object {$_ -split ','}|Where-Object {$_})
$GroupColumns=@($GroupColumns|ForEach-Object {$_ -split ','}|Where-Object {$_})
$rows=@(Import-Csv -LiteralPath $InputCsv)
if(-not $rows.Count){throw "No rows in $InputCsv"}
if(-not $OutputCsv){$OutputCsv=[IO.Path]::ChangeExtension($InputCsv,'summary.csv')}
foreach($column in @($MetricColumns)+@($GroupColumns)){
    if(-not $rows[0].PSObject.Properties[$column]){throw "Column '$column' missing"}
}
$groups=if($GroupColumns.Count){@($rows|Group-Object -Property $GroupColumns)}else{
    @([pscustomobject]@{Name='All';Group=$rows})
}
$summary=[Collections.Generic.List[object]]::new()
foreach($group in $groups){
    foreach($metric in $MetricColumns){
        $values=@($group.Group|ForEach-Object {
            if($_.$metric -ne ''){
                [double]::Parse($_.$metric,[Globalization.NumberStyles]::Float,
                    [Globalization.CultureInfo]::InvariantCulture)
            }
        }|Sort-Object)
        if(-not $values.Count){continue}
        $count=$values.Count
        $mean=($values|Measure-Object -Average).Average
        $median=if($count%2){$values[[int][math]::Floor($count/2)]}else{
            ($values[$count/2-1]+$values[$count/2])/2
        }
        $variance=0.0
        foreach($v in $values){$variance+=($v-$mean)*($v-$mean)}
        $sampleStd=if($count -gt 1){[math]::Sqrt($variance/($count-1))}else{0.0}
        $p95Index=0.95*($count-1)
        $lo=[int][math]::Floor($p95Index)
        $hi=[int][math]::Ceiling($p95Index)
        $p95=$values[$lo]+($values[$hi]-$values[$lo])*($p95Index-$lo)
        $summary.Add([pscustomobject]@{
            Group=$group.Name;Metric=$metric;Samples=$count
            Mean=[math]::Round($mean,5);Median=[math]::Round($median,5)
            SampleStdDev=[math]::Round($sampleStd,5)
            Minimum=$values[0];Maximum=$values[$count-1]
            P95=[math]::Round($p95,5)
        })
    }
}
$summary|Export-Csv -LiteralPath $OutputCsv -NoTypeInformation -Encoding UTF8
Write-Host "[Profiler] Numeric summary: $OutputCsv"
