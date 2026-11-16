FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["ZorgUitwisseling.Api/ZorgUitwisseling.Api.csproj", "ZorgUitwisseling.Api/"]
RUN dotnet restore "ZorgUitwisseling.Api/ZorgUitwisseling.Api.csproj"
COPY ZorgUitwisseling.Api/ ZorgUitwisseling.Api/
RUN dotnet publish "ZorgUitwisseling.Api/ZorgUitwisseling.Api.csproj" --no-restore -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN mkdir /app/data && chown app:app /app/data
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "ZorgUitwisseling.Api.dll"]
