Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$outputPath = "C:\Users\Administrator.DESKTOP-F9T4GKP\Desktop\key_bridge\probe_text.txt"
[System.IO.File]::WriteAllText($outputPath, "")

$form = New-Object System.Windows.Forms.Form
$form.Text = "BlueType Probe"
$form.Width = 900
$form.Height = 700
$form.StartPosition = "CenterScreen"
$form.TopMost = $true

$textBox = New-Object System.Windows.Forms.TextBox
$textBox.Multiline = $true
$textBox.AcceptsReturn = $true
$textBox.AcceptsTab = $true
$textBox.ScrollBars = "Both"
$textBox.Dock = "Fill"
$textBox.Font = New-Object System.Drawing.Font("Consolas", 18)
$form.Controls.Add($textBox)

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 200
$timer.Add_Tick({
    if (-not $textBox.Focused) {
        $form.Activate()
        $textBox.Focus()
    }
    [System.IO.File]::WriteAllText($outputPath, $textBox.Text)
})
$timer.Start()

$form.Add_Shown({
    $form.Activate()
    $textBox.Focus()
})

[System.Windows.Forms.Application]::Run($form)
