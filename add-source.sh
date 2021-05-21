#!/bin/bash

dotnet nuget add source https://packages.antvoice.com/repository/nuget-hosted/ \
	-n antvoice-nuget-hosted -u ${USERNAME} -p ${PASSWORD} \
	--store-password-in-clear-text
