@for %%a in (*.mid) do @midisplit "%%a" "%%~na-split.mid"

@rem @set midisplit_dir=%~dp0
@rem 
@rem :dispatch_loop
@rem   @if "%1"=="" @goto dispatch_done
@rem   @echo %midisplit_dir%midisplit "%1" "%~dpn1.mid"
@rem   @shift
@rem   @goto dispatch_loop
@rem :dispatch_done
