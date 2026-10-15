$ff = "$env:USERPROFILE/AppData/Local/ms-playwright/ffmpeg-1011/ffmpeg-win64.exe"
$w  = "c:/Users/1/Documents/UmirHackCaseOrange/SmartInventory/brag-output/work"
$out= "c:/Users/1/Documents/UmirHackCaseOrange/SmartInventory/brag-output"

# encode frames + audio -> brag.mp4 (crf 19, yuv420p, faststart)
& $ff -y -framerate 30 -i "$w/frames/f%04d.jpg" -i "$w/audio.wav" `
  -c:v libx264 -pix_fmt yuv420p -crf 19 -preset medium `
  -c:a aac -b:a 160k -shortest -movflags +faststart "$out/brag.mp4"
Write-Output "mp4 exit $LASTEXITCODE"

# poster = the settled dashboard+tagline frame (~t=2.6 -> f0078)
Copy-Item "$w/frames/f0078.jpg" "$out/brag.jpg" -Force
Write-Output "poster done"
