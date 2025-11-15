FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=80
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["EasyFortniteStats-ImageApi.csproj", "./"]
RUN dotnet restore "EasyFortniteStats-ImageApi.csproj"
COPY . .
WORKDIR "/src/"
RUN dotnet build "EasyFortniteStats-ImageApi.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "EasyFortniteStats-ImageApi.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EasyFortniteStats-ImageApi.dll"]