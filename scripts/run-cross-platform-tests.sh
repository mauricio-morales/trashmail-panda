#!/bin/bash
# Script to run cross-platform tests using Docker

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}🔄 Running Cross-Platform Tests in Docker${NC}"
echo "=============================================="

# Check if Docker is available
if ! command -v docker &> /dev/null; then
    echo -e "${RED}❌ Docker is not installed or not in PATH${NC}"
    exit 1
fi

# Create results directories
mkdir -p TestResults/CrossPlatform
mkdir -p coverage/CrossPlatform

# Build and run cross-platform tests
echo -e "${YELLOW}📦 Building cross-platform test container...${NC}"
docker-compose -f docker-compose.tests.yml build cross-platform-tests

echo -e "${YELLOW}🧪 Running cross-platform unit and integration tests...${NC}"
if docker-compose -f docker-compose.tests.yml run --rm cross-platform-tests; then
    echo -e "${GREEN}✅ Cross-platform tests passed!${NC}"

    # Show test results if available
    if [ -d "TestResults/CrossPlatform" ] && [ "$(ls -A TestResults/CrossPlatform)" ]; then
        echo -e "${BLUE}📊 Test Results Summary:${NC}"
        find TestResults/CrossPlatform -name "*.trx" -exec echo "  📄 {}" \;
    fi

    # Show coverage if available
    if [ -d "coverage/CrossPlatform" ] && [ "$(ls -A coverage/CrossPlatform)" ]; then
        echo -e "${BLUE}📈 Coverage Reports:${NC}"
        find coverage/CrossPlatform -name "*.xml" -exec echo "  📊 {}" \;
    fi

    exit 0
else
    echo -e "${RED}❌ Cross-platform tests failed${NC}"

    # Check for hang dumps
    echo -e "${YELLOW}🔍 Checking for hang dumps...${NC}"
    if find TestResults/CrossPlatform -name "Sequence_*.xml" -o -name "*hangdump*" 2>/dev/null | grep -q .; then
        echo -e "${BLUE}📋 Hang dump files found:${NC}"
        find TestResults/CrossPlatform -name "Sequence_*.xml" -o -name "*hangdump*" 2>/dev/null | while read -r dumpfile; do
            echo -e "${BLUE}  📄 ${dumpfile}${NC}"
            if [[ "$dumpfile" == *.xml ]]; then
                echo -e "${YELLOW}  Content preview:${NC}"
                head -50 "$dumpfile" | grep -E "(TestName|MethodName|ClassName|StackTrace)" || cat "$dumpfile" | head -50
            fi
        done
    else
        echo -e "${YELLOW}No hang dumps found. Test may have failed for other reasons.${NC}"
    fi

    exit 1
fi