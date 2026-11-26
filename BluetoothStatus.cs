using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

internal static class BluetoothStatus {
    internal sealed class Entry { internal bool Connected; internal string Name; }
    const string Source=@"$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'

Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null=[Windows.Devices.Enumeration.DeviceInformation,Windows.Devices.Enumeration,ContentType=WindowsRuntime]
$null=[Windows.Devices.Enumeration.DeviceInformationCollection,Windows.Devices.Enumeration,ContentType=WindowsRuntime]
$null=[Windows.Devices.Enumeration.DeviceInformationKind,Windows.Devices.Enumeration,ContentType=WindowsRuntime]
$null=[Windows.Devices.Bluetooth.BluetoothDevice,Windows.Devices.Bluetooth,ContentType=WindowsRuntime]
$null=[Windows.Devices.Bluetooth.BluetoothLEDevice,Windows.Devices.Bluetooth,ContentType=WindowsRuntime]
$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {$_.Name -eq 'AsTask' -and $_.IsGenericMethodDefinition -and $_.GetGenericArguments().Count -eq 1 -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'} | Select-Object -First 1

$null=[Windows.Devices.Bluetooth.BluetoothConnectionStatus,Windows.Devices.Bluetooth,ContentType=WindowsRuntime]
$tasks=@()
foreach($type in @([Windows.Devices.Bluetooth.BluetoothDevice],[Windows.Devices.Bluetooth.BluetoothLEDevice])) {
  foreach($st in @([Windows.Devices.Bluetooth.BluetoothConnectionStatus]::Connected,[Windows.Devices.Bluetooth.BluetoothConnectionStatus]::Disconnected)) {
    $q='('+$type::GetDeviceSelectorFromConnectionStatus($st)+') AND ('+$type::GetDeviceSelectorFromPairingState($true)+')'
    $op=[Windows.Devices.Enumeration.DeviceInformation]::FindAllAsync($q,[string[]]@(),[Windows.Devices.Enumeration.DeviceInformationKind]::AssociationEndpoint)
    $tasks+=@(@{ Task=$asTask.MakeGenericMethod([Windows.Devices.Enumeration.DeviceInformationCollection]).Invoke($null,@($op)); Online=($st -eq 'Connected') })
  }
}
foreach($x in $tasks) {
 if(!$x.Task.Wait(3500)){continue}
 foreach($d in $x.Task.Result) {
    if($d.Id -match '-((?:[0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2})$') {
       $mac=$Matches[1].Replace(':','').ToUpperInvariant()
       $display=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$d.Name))
       [Console]::WriteLine($mac+'|'+[int]$x.Online+'|'+$display)
    }
 }
}
";
    internal static Dictionary<string,Entry> Read() {
        var rows=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
        try {
            var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","powershell.exe");
            var pInfo=new ProcessStartInfo(path,"-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(Source))){
                CreateNoWindow=true,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var child=Process.Start(pInfo)) {
                var stdout=child.StandardOutput.ReadToEndAsync();
                var stderr=child.StandardError.ReadToEndAsync();
                if(!child.WaitForExit(6500)) {try {child.Kill();}catch(InvalidOperationException){} return rows;}
                if(child.ExitCode!=0||!stdout.Wait(700)||!stderr.Wait(700))return rows;
                foreach(var line in stdout.Result.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)) {
                    var fields=line.Split('|');
                    if(fields.Length!=3||!Regex.IsMatch(fields[0],@"\A[0-9A-F]{12}\z")||(fields[1]!="0"&&fields[1]!="1"))continue;
                    string name;
                    try{name=Encoding.UTF8.GetString(Convert.FromBase64String(fields[2])).Replace("\r"," ").Replace("\n"," ");}
                    catch(FormatException){continue;}
                    bool online=fields[1]=="1";Entry previous;
                    if(!rows.TryGetValue(fields[0],out previous)||(!previous.Connected&&online))
                        rows[fields[0]]=new Entry{Connected=online,Name=name};
                }
            }
        } catch(Exception) { /* SetupAPI still provides independent fallback. */ }
        return rows;
    }
}
