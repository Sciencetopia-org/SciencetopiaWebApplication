FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
ARG HTTP_PROXY
ARG HTTPS_PROXY
ARG NO_PROXY
ENV HTTP_PROXY=$HTTP_PROXY \
    HTTPS_PROXY=$HTTPS_PROXY \
    NO_PROXY=$NO_PROXY \
    http_proxy=$HTTP_PROXY \
    https_proxy=$HTTPS_PROXY \
    no_proxy=$NO_PROXY
COPY SciencetopiaWebApplication.csproj ./
ARG NUGET_SOURCE
RUN if [ -n "$NUGET_SOURCE" ]; then dotnet restore "SciencetopiaWebApplication.csproj" --source "$NUGET_SOURCE"; else dotnet restore "SciencetopiaWebApplication.csproj"; fi
COPY . .
RUN dotnet publish "SciencetopiaWebApplication.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "SciencetopiaWebApplication.dll"]
