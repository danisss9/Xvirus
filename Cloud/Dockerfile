FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update && apt-get install -y \
    curl \
    ca-certificates \
    --no-install-recommends \
 && curl -fsSL https://deb.nodesource.com/setup_24.x | bash - \
 && apt-get install -y nodejs \
 && apt-get clean \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /usr/src/app

COPY package*.json ./
RUN npm install --omit=dev

# The script AI model (sdk/Database/scriptmodel.gguf, ~940 MB) is not stored in the
# repository; download it into the image at build time from the update server. It is
# fetched before COPY so the 940 MB layer stays cached across source-only rebuilds.
RUN mkdir -p sdk/Database \
 && curl -fSL --retry 3 -o sdk/Database/scriptmodel.gguf "https://files.xvirus.net/api/public/dl/Um0CRbLL" \
 && head -c 4 sdk/Database/scriptmodel.gguf | grep -q GGUF

COPY . .

EXPOSE 80

CMD ["node", "main.js"]
