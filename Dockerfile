FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore EnglishLearningPlatform.sln
RUN dotnet publish src/EnglishLearningPlatform.Web/EnglishLearningPlatform.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG ELP_CANDIDATE_SHA=development
LABEL org.opencontainers.image.revision=$ELP_CANDIDATE_SHA
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
# Audio và ảnh đại diện nằm ngoài wwwroot; named volume giữ file khi rebuild container.
RUN mkdir -p /app/App_Data/stimulus-audio /app/App_Data/avatars && chown -R app:app /app/App_Data
USER app
ENTRYPOINT ["dotnet", "EnglishLearningPlatform.Web.dll"]
