FROM mcr.microsoft.com/dotnet/aspnet:8.0
 
COPY Web/bin/Release/net8.0/publish /app
 
WORKDIR /app

ENTRYPOINT ["dotnet", "Web.dll"]

# donet publish
# docker buildx build --platform linux/amd64,linux/arm64 -t docker.killf.info:5000/comfyplus/web .  --push

# docker build -t docker.killf.info:5000/comfyplus/web .    
# docker push docker.killf.info:5000/comfyplus/web
# docker run --rm -it -p 8000:8080 docker.killf.info:5000/comfyplus/web

# 宝塔面板: https://43.134.68.113:23579/tencentcloud