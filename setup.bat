@call vars.bat
paket.bootstrapper
paket install
set path=%cd%\packages\proj2src\lib\net452;%path%
proj2src paket-files\jbevain\cecil\Mono.Cecil.csproj