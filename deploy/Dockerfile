# Gebeta server image (abay/gebeta-server), built by CI from the repository root:
#   docker build -f deploy/Dockerfile -t abay/gebeta-server:1.0.0 .
# One image holds every module; Modules__Enabled__N picks the ones a container runs.
# The same image runs migrations: `--migrate` (the migrate container).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
RUN dotnet publish src/Gebeta.Api/Gebeta.Api.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0 \
    TZ=Africa/Addis_Ababa
EXPOSE 8080
# The aspnet image has a non-root "app" user.
USER app
HEALTHCHECK --interval=15s --timeout=5s --retries=10 \
  CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && printf "GET /health/live HTTP/1.0\r\n\r\n" >&3 && grep -q "200 OK" <&3' || exit 1
ENTRYPOINT ["dotnet", "Gebeta.Api.dll"]
