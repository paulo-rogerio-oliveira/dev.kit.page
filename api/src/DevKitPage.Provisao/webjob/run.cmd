@echo off
rem O WebJob continuo do worker da provisao (App Service Windows): o Kudu roda este arquivo na copia do job.
dotnet DevKitPage.Provisao.dll
