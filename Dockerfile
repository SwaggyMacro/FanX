FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base

# set work dir
WORKDIR /app

# install requirements
RUN apt-get update && \
    apt-get install -y \
    ipmitool \
    wget \
    unzip \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# create data dir
RUN mkdir -p /app/data /app/logs && \
    chmod 755 /app/data /app/logs

# set env
ENV ASPNETCORE_URLS=http://+:5136
ENV FanX_PORT=5136

# download and unzip
ARG TARGETARCH
RUN if [ "$TARGETARCH" = "amd64" ]; then \
        wget -O fanx.zip https://github.com/SwaggyMacro/FanX/releases/latest/download/FanX.Linux.x64.zip; \
    elif [ "$TARGETARCH" = "arm64" ]; then \
        wget -O fanx.zip https://github.com/SwaggyMacro/FanX/releases/latest/download/FanX.Linux.arm64.zip; \
    else \
        echo "Unsupported architecture: $TARGETARCH" && exit 1; \
    fi && \
    unzip fanx.zip && \
    rm fanx.zip && \
    chmod +x FanX

# link file
RUN ln -sf /app/data/FanX.db /app/FanX.db

RUN ln -sf /app/logs /app/Logs

# volume
VOLUME ["/app/data", "/app/logs"]

# expose port
EXPOSE 5136

# run
CMD ["./FanX"]
