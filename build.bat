cls
pushd scripts
install-dependencies.bat
popd
gitversion /l console /updateassemblyinfo /exec "scripts\lib\FAKE\tools\FAKE.exe" /execArgs "scripts\build.fsx %*"
