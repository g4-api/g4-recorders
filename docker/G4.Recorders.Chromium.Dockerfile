# G4 Chromium recorder image.
#
# One container holds everything a recording needs:
#   - the recorder host (ASP.NET, port 9956)
#   - Chrome for Testing with the recorder extension (launched by the recorder host)
#   - a virtual screen with VNC (5900) and noVNC (7900), the same stack the Selenium Grid nodes use
#
# Build from the repository root:
#   docker build -f ./docker/G4.Recorders.Chromium.Dockerfile -t g4api/g4-chromium-recorder .

# ---------- Build stage: publish the recorder host ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# Set the build configuration (default is Release)
ARG BUILD_CONFIGURATION=Release

WORKDIR /src

# Copy the sources. The recorder extension is a sibling folder that the project links into its output.
COPY ["src/", "/src"]

# Publish the recorder host (framework-dependent, the runtime stage supplies ASP.NET)
RUN dotnet publish "G4.Recorders.Chromium/G4.Recorders.Chromium.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish

# ---------- Runtime source: ASP.NET shared framework ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS aspnet

# ---------- Runtime stage: Selenium display stack + Chrome for Testing + recorder ----------
FROM selenium/node-base:latest AS runtime

# Chrome for Testing channel (Stable, Beta, Dev, Canary) to install
ARG CHROME_CHANNEL=Stable

USER root

# Install the libraries Chrome needs. Chrome for Testing is a plain archive, so apt does not
# resolve them for us. libicu is needed by .NET globalization.
RUN apt-get update -qq \
    && apt-get install -y --no-install-recommends \
        fonts-liberation \
        libasound2t64 \
        libatk-bridge2.0-0t64 \
        libatk1.0-0t64 \
        libatspi2.0-0t64 \
        libcairo2 \
        libcups2t64 \
        libdbus-1-3 \
        libdrm2 \
        libgbm1 \
        libglib2.0-0t64 \
        libgtk-3-0t64 \
        libnspr4 \
        libnss3 \
        libpango-1.0-0 \
        libvulkan1 \
        libx11-6 \
        libxcb1 \
        libxcomposite1 \
        libxdamage1 \
        libxext6 \
        libxfixes3 \
        libxkbcommon0 \
        libxrandr2 \
        xdg-utils \
        "libicu[0-9][0-9]" \
    && rm -rf /var/lib/apt/lists/*

# Install Chrome for Testing. Branded Chrome blocks the load-extension argument the recorder
# relies on; Chrome for Testing still allows it.
RUN set -eux; \
    CHROME_URL="$(curl -fsSL https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions-with-downloads.json \
        | python3 -c "import json,sys; d=json.load(sys.stdin)['channels']['${CHROME_CHANNEL}']['downloads']['chrome']; print([i['url'] for i in d if i['platform']=='linux64'][0])")"; \
    curl -fsSL "$CHROME_URL" -o /tmp/chrome.zip; \
    unzip -q /tmp/chrome.zip -d /opt; \
    mv /opt/chrome-linux64 /opt/chrome-for-testing; \
    rm /tmp/chrome.zip

# Install the ASP.NET runtime by copying it from the official runtime image
COPY --from=aspnet /usr/share/dotnet /usr/share/dotnet
RUN ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet

# Lay the files out like a G4 sandbox, so the recorder finds its browser without any configuration:
#   /g4/bot-utilities/chromium-recorder-x64   recorder host
#   /g4/browsers/chrome/chrome                browser (wrapper around Chrome for Testing)
ENV G4_SANDBOX=/g4
COPY --from=build /app/publish /g4/bot-utilities/chromium-recorder-x64
COPY docker/chromium-recorder/chrome /g4/browsers/chrome/chrome

# Swap the Selenium node process for the recorder host and drop the Grid video programs.
# The display programs (xvfb, vnc, novnc) stay as the base image defines them.
COPY docker/chromium-recorder/g4-recorder.conf /etc/supervisor/conf.d/g4-recorder.conf
RUN sed -i '/^\[program:selenium-node\]/,$d' /etc/supervisor/conf.d/selenium.conf \
    && rm -f /etc/supervisor/conf.d/recorder.conf /etc/supervisor/conf.d/uploader.conf \
    && sed -i 's/\r$//' /g4/browsers/chrome/chrome /etc/supervisor/conf.d/g4-recorder.conf \
    && chmod +x /g4/browsers/chrome/chrome \
    && chown -R 1200:0 /g4 \
    && chmod -R g=u /g4

USER 1200

# Recorder API, VNC and noVNC
EXPOSE 9956 5900 7900
