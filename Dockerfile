FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY src/MediaOrganizer/MediaOrganizer.csproj src/MediaOrganizer/
RUN dotnet restore src/MediaOrganizer/MediaOrganizer.csproj

# Copy everything else and build
COPY src/MediaOrganizer/ src/MediaOrganizer/
RUN dotnet publish src/MediaOrganizer/MediaOrganizer.csproj -c Release -o /app/publish

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Set timezone to your local timezone (change as needed)
ENV TZ=Europe/Amsterdam
RUN ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone

COPY --from=build /app/publish .

EXPOSE 45263

ENTRYPOINT ["dotnet", "MediaOrganizer.dll"]
