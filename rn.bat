@echo off
if "%~1"=="" (
    echo Need first parameter: module name
    goto exit
)
if "%~2"=="" (
    echo Need second parameter: ModuleID
    goto exit
)
ren "Assets\ModuleNameModule.cs" "%~2Module.cs"
ren "Assets\ModuleNameModule.cs.meta" "%~2Module.cs.meta"
attrib +h "Assets\%~2Module.cs.meta"
ren "Assets\ModuleNameModule.prefab" "%~2Module.prefab"
ren "Assets\ModuleNameModule.prefab.meta" "%~2Module.prefab.meta"
attrib +h "Assets\%~2Module.prefab.meta"
ren "Assets\ModuleName.unity" "%~2.unity"

:exit
