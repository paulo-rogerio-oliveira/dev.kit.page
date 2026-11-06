#!/bin/bash
# O WebJob contínuo do worker da provisão (App Service Linux): o Kudu roda este arquivo na cópia do job.
cd "$(dirname "$0")"
exec dotnet DevKitPage.Provisao.dll
