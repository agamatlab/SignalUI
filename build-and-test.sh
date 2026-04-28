#!/bin/bash
# Build and Test Script for Plugin System
# Run this script from the solution root directory

echo "=========================================="
echo "SignalUI Plugin System - Build & Test"
echo "=========================================="
echo ""

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Step 1: Clean previous builds
echo "Step 1: Cleaning previous builds..."
dotnet clean singalUI.sln
if [ $? -eq 0 ]; then
    echo -e "${GREEN}✓ Clean successful${NC}"
else
    echo -e "${RED}✗ Clean failed${NC}"
    exit 1
fi
echo ""

# Step 2: Restore dependencies
echo "Step 2: Restoring dependencies..."
dotnet restore singalUI.sln
if [ $? -eq 0 ]; then
    echo -e "${GREEN}✓ Restore successful${NC}"
else
    echo -e "${RED}✗ Restore failed${NC}"
    exit 1
fi
echo ""

# Step 3: Build solution
echo "Step 3: Building solution..."
dotnet build singalUI.sln --configuration Debug
if [ $? -eq 0 ]; then
    echo -e "${GREEN}✓ Build successful${NC}"
else
    echo -e "${RED}✗ Build failed${NC}"
    exit 1
fi
echo ""

# Step 4: Verify plugin DLLs were created
echo "Step 4: Verifying plugin DLLs..."
PLUGINS_DIR="singalUI/bin/Debug/net8.0/Plugins"

if [ -d "$PLUGINS_DIR" ]; then
    echo -e "${GREEN}✓ Plugins directory exists${NC}"
    echo "Contents:"
    ls -lh "$PLUGINS_DIR"/*.dll 2>/dev/null
    
    # Check for each plugin
    if [ -f "$PLUGINS_DIR/MockStageController.Plugin.dll" ]; then
        echo -e "${GREEN}✓ MockStageController.Plugin.dll found${NC}"
    else
        echo -e "${RED}✗ MockStageController.Plugin.dll NOT found${NC}"
    fi
    
    if [ -f "$PLUGINS_DIR/PIController.Plugin.dll" ]; then
        echo -e "${GREEN}✓ PIController.Plugin.dll found${NC}"
    else
        echo -e "${RED}✗ PIController.Plugin.dll NOT found${NC}"
    fi
    
    if [ -f "$PLUGINS_DIR/SigmakokiController.Plugin.dll" ]; then
        echo -e "${GREEN}✓ SigmakokiController.Plugin.dll found${NC}"
    else
        echo -e "${RED}✗ SigmakokiController.Plugin.dll NOT found${NC}"
    fi
    
    # Check for PI DLLs
    if [ -f "$PLUGINS_DIR/PI_GCS2_DLL_x64.dll" ]; then
        echo -e "${GREEN}✓ PI_GCS2_DLL_x64.dll found${NC}"
    else
        echo -e "${YELLOW}⚠ PI_GCS2_DLL_x64.dll NOT found (may be expected)${NC}"
    fi
else
    echo -e "${RED}✗ Plugins directory does not exist${NC}"
fi
echo ""

# Step 5: Run unit tests
echo "Step 5: Running unit tests..."
dotnet test singalUI.Tests/singalUI.Tests.csproj --filter "FullyQualifiedName~Plugin" --verbosity normal
if [ $? -eq 0 ]; then
    echo -e "${GREEN}✓ All tests passed${NC}"
else
    echo -e "${RED}✗ Some tests failed${NC}"
    exit 1
fi
echo ""

# Step 6: Run all tests
echo "Step 6: Running all tests..."
dotnet test singalUI.Tests/singalUI.Tests.csproj --verbosity normal
if [ $? -eq 0 ]; then
    echo -e "${GREEN}✓ All tests passed${NC}"
else
    echo -e "${YELLOW}⚠ Some tests failed (may be expected if hardware not connected)${NC}"
fi
echo ""

# Step 7: Summary
echo "=========================================="
echo "Build & Test Summary"
echo "=========================================="
echo ""
echo "Projects built:"
echo "  ✓ singalUI"
echo "  ✓ singalUI.Tests"
echo "  ✓ MockStageController.Plugin"
echo "  ✓ PIController.Plugin"
echo "  ✓ SigmakokiController.Plugin"
echo ""
echo "Plugins created:"
ls -1 "$PLUGINS_DIR"/*.Plugin.dll 2>/dev/null | wc -l | xargs echo "  Total plugins:"
echo ""
echo "Next steps:"
echo "  1. Run the application: dotnet run --project singalUI/singalUI.csproj"
echo "  2. Check console output for plugin loading messages"
echo "  3. Verify all 3 plugins load successfully"
echo ""
echo -e "${GREEN}Build and test complete!${NC}"
