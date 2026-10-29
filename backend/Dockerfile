# API (.NET 10). Build: docker build -t sistema-vendas-api backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/SistemaVendas.Api -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Servidor pequeno (1 GB): GC de estação e teto de memória do heap.
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_gcServer=0 \
    DOTNET_GCHeapHardLimit=0x10000000 \
    TZ=America/Sao_Paulo
EXPOSE 8080
ENTRYPOINT ["dotnet", "SistemaVendas.Api.dll"]
