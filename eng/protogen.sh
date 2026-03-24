#!/bin/bash
CHECK="\u2714"
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[1;31m'
NC='\033[0m' # No Color

# Tools required:
#
# C# (Grpc.Tools / MSBuild — no manual protoc call needed for C#):
#   Add to Messaging.Contracts.csproj:
#     <PackageReference Include="Google.Protobuf" Version="3.*" />
#     <PackageReference Include="Grpc.Tools" Version="2.*" PrivateAssets="All" />
#     <Protobuf Include="ProtoSchemas/**/*.proto" GrpcServices="None" />
#   Then run: dotnet build
#
# TypeScript (ts-proto via npm):
#   npm install -g ts-proto          # or: npx ts-proto
#   npm install -g grpc-tools        # provides protoc binary if not installed
#   https://github.com/stephenh/ts-proto
#
# C++ (protoc — standard Protobuf compiler):
#   https://github.com/protocolbuffers/protobuf/releases
#   (install protoc and add to PATH)

protoFrom="./src/Messaging.Contracts/ProtoSchemas"

if [ ! -d "$protoFrom" ]; then
    echo -e "${RED}ERROR: ProtoSchemas directory not found: ${protoFrom}${NC}"; exit 1
fi

protoFiles=()
while IFS= read -r -d '' f; do
    protoFiles+=("$f")
done < <(find "$protoFrom" -type f -name "*.proto" -print0)

if [ ${#protoFiles[@]} -eq 0 ]; then
    echo -e "${RED}ERROR: no .proto files found in ${protoFrom}${NC}"; exit 1
fi

echo -e "${NC}Found ${#protoFiles[@]} .proto file(s) in ${protoFrom}"

# ── C# ────────────────────────────────────────────────────────────────────────
# C# code generation is handled automatically by Grpc.Tools during dotnet build.
# Run: dotnet build ./src/Messaging.Contracts/Messaging.Contracts.csproj
echo -e "${GREEN}${CHECK}  C# — handled by Grpc.Tools (run: dotnet build)${NC}"

# ── TypeScript ────────────────────────────────────────────────────────────────
# Requires: protoc + ts-proto plugin (npm install -g ts-proto)

tsTo="./generated/ts/proto"
mkdir -p "$tsTo"
echo -e "${NC}Generating TypeScript schemas..."

PROTOC=""
if   command -v protoc &>/dev/null; then PROTOC=protoc
else
    echo -e "${YELLOW}WARNING: protoc not found. Skipping TypeScript generation.${NC}"
    echo -e "${YELLOW}         Install from https://github.com/protocolbuffers/protobuf/releases${NC}"
    PROTOC=""
fi

if [ -n "$PROTOC" ]; then
    # Resolve ts-proto plugin
    TS_PROTO_PLUGIN=""
    if   command -v protoc-gen-ts_proto &>/dev/null; then
        TS_PROTO_PLUGIN="protoc-gen-ts_proto=$(command -v protoc-gen-ts_proto)"
    elif [ -f "./node_modules/.bin/protoc-gen-ts_proto" ]; then
        TS_PROTO_PLUGIN="protoc-gen-ts_proto=./node_modules/.bin/protoc-gen-ts_proto"
    fi

    if [ -z "$TS_PROTO_PLUGIN" ]; then
        echo -e "${YELLOW}WARNING: protoc-gen-ts_proto not found. Skipping TypeScript generation.${NC}"
        echo -e "${YELLOW}         Install with: npm install -g ts-proto${NC}"
    else
        $PROTOC \
            --proto_path="$protoFrom" \
            --plugin="$TS_PROTO_PLUGIN" \
            --ts_proto_out="$tsTo" \
            --ts_proto_opt=esModuleInterop=true \
            "${protoFiles[@]}"
        if [ $? -ne 0 ]; then
            echo -e "${RED}ERROR: TypeScript generation failed${NC}"; exit 1
        fi
        echo -e "${GREEN}${CHECK}  Generated TypeScript → ${tsTo}${NC}"
    fi
fi

# ── C++ ───────────────────────────────────────────────────────────────────────
# Requires: protoc

cppTo="./generated/cpp/proto"
mkdir -p "$cppTo"
echo -e "${NC}Generating C++ schemas..."

if [ -z "$PROTOC" ]; then
    echo -e "${RED}ERROR: protoc not found. Cannot generate C++ schemas.${NC}"
    echo -e "${RED}       Install from https://github.com/protocolbuffers/protobuf/releases${NC}"
    exit 1
fi

$PROTOC \
    --proto_path="$protoFrom" \
    --cpp_out="$cppTo" \
    "${protoFiles[@]}"
if [ $? -ne 0 ]; then
    echo -e "${RED}ERROR: C++ generation failed${NC}"; exit 1
fi
echo -e "${GREEN}${CHECK}  Generated C++ → ${cppTo}${NC}"

echo -e "${NC}Done."
