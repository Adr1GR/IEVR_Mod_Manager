#!/bin/bash
# Script para compilar y crear un AppImage para Linux
# Uso: ./build-linux.sh [version]
# Ejemplo: ./build-linux.sh 1.8.0

set -e

VERSION="${1:-}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=== IE:VR Mod Manager - Linux Build Script ==="
echo ""

# Si no se proporciona versión, pedirla
if [ -z "$VERSION" ]; then
    read -p "Ingresa la nueva versión (ej: 1.8.0): " VERSION
fi

# Validar formato de versión
if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Error: La versión debe tener el formato X.Y.Z (ej: 1.8.0)"
    exit 1
fi

echo "Versión a compilar: $VERSION"
echo ""

# Verificar que estamos en Linux
if [[ "$OSTYPE" != "linux-gnu"* ]]; then
    echo "Advertencia: Este script está diseñado para ejecutarse en Linux"
    echo "Para compilar desde Windows, usa WSL o una máquina virtual Linux"
    echo ""
fi

# Verificar que dotnet está instalado
if ! command -v dotnet &> /dev/null; then
    echo "Error: .NET SDK no está instalado"
    echo "Instala .NET 8 SDK desde: https://dotnet.microsoft.com/download"
    exit 1
fi

# Verificar versión de .NET
DOTNET_VERSION=$(dotnet --version)
echo "Versión de .NET: $DOTNET_VERSION"
echo ""

# Actualizar versión en .csproj
echo "1. Actualizando versión en IEVRModManager.csproj..."
CSPROJ_FILE="IEVRModManager.csproj"
if [ ! -f "$CSPROJ_FILE" ]; then
    echo "Error: No se encontró el archivo IEVRModManager.csproj"
    exit 1
fi

# Actualizar versiones usando sed (compatible con Linux)
sed -i "s/<Version>.*<\/Version>/<Version>$VERSION<\/Version>/" "$CSPROJ_FILE"
sed -i "s/<AssemblyVersion>.*<\/AssemblyVersion>/<AssemblyVersion>$VERSION.0<\/AssemblyVersion>/" "$CSPROJ_FILE"
sed -i "s/<FileVersion>.*<\/FileVersion>/<FileVersion>$VERSION.0<\/FileVersion>/" "$CSPROJ_FILE"
sed -i "s/<InformationalVersion>.*<\/InformationalVersion>/<InformationalVersion>$VERSION<\/InformationalVersion>/" "$CSPROJ_FILE"

echo "   Versión actualizada"
echo ""

# Limpiar builds anteriores
echo "2. Limpiando builds anteriores..."
dotnet clean -c Release 2>&1 | grep -v "warning\|info" || true
echo "   Limpieza completada"
echo ""

# Compilar para Linux
echo "3. Compilando aplicación para Linux..."
BUILD_RESULT=$(dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true 2>&1)

if [ $? -ne 0 ]; then
    echo "Error durante la compilación:"
    echo "$BUILD_RESULT"
    exit 1
fi

echo "   Compilación completada"
echo ""

# Verificar que el ejecutable existe (intentar diferentes rutas posibles)
EXE_PATH=""
if [ -f "bin/Release/net8.0/linux-x64/publish/IEVRModManager" ]; then
    EXE_PATH="bin/Release/net8.0/linux-x64/publish/IEVRModManager"
elif [ -f "bin/Release/net8.0-windows/linux-x64/publish/IEVRModManager" ]; then
    EXE_PATH="bin/Release/net8.0-windows/linux-x64/publish/IEVRModManager"
else
    echo "Error: No se encontró el ejecutable"
    echo "Nota: Si estás usando WPF, necesitas migrar a Avalonia UI primero"
    echo "      Ver MIGRATION_TO_LINUX.md para más información"
    exit 1
fi

EXE_SIZE=$(du -h "$EXE_PATH" | cut -f1)
echo "4. Ejecutable generado:"
echo "   Ubicación: $EXE_PATH"
echo "   Tamaño: $EXE_SIZE"
echo ""

# Crear estructura para AppImage
echo "5. Preparando estructura para AppImage..."
APPIMAGE_DIR="AppDir"
if [ -d "$APPIMAGE_DIR" ]; then
    rm -rf "$APPIMAGE_DIR"
fi

mkdir -p "$APPIMAGE_DIR/usr/bin"
mkdir -p "$APPIMAGE_DIR/usr/share/applications"
mkdir -p "$APPIMAGE_DIR/usr/share/icons/hicolor/512x512/apps"
mkdir -p "$APPIMAGE_DIR/usr/share/pixmaps"

# Copiar ejecutable
cp "$EXE_PATH" "$APPIMAGE_DIR/usr/bin/IEVRModManager"
chmod +x "$APPIMAGE_DIR/usr/bin/IEVRModManager"

# Copiar icono si existe
if [ -f "logo.ico" ]; then
    # Convertir ICO a PNG (requiere ImageMagick)
    if command -v convert &> /dev/null; then
        convert logo.ico -resize 512x512 "$APPIMAGE_DIR/usr/share/icons/hicolor/512x512/apps/IEVRModManager.png" 2>/dev/null || \
        cp logo.ico "$APPIMAGE_DIR/usr/share/icons/hicolor/512x512/apps/IEVRModManager.ico" 2>/dev/null || true
    fi
fi

# Crear archivo .desktop
cat > "$APPIMAGE_DIR/usr/share/applications/IEVRModManager.desktop" << EOF
[Desktop Entry]
Name=IE:VR Mod Manager
Comment=Mod Manager for Inazuma Eleven Victory Road
Exec=IEVRModManager
Icon=IEVRModManager
Type=Application
Categories=Game;Utility;
StartupNotify=true
EOF

# Crear AppRun
cat > "$APPIMAGE_DIR/AppRun" << 'EOF'
#!/bin/bash
HERE="$(dirname "$(readlink -f "${0}")")"
exec "${HERE}/usr/bin/IEVRModManager" "$@"
EOF
chmod +x "$APPIMAGE_DIR/AppRun"

echo "   Estructura creada"
echo ""

# Descargar appimagetool si no existe
APPIMAGETOOL="appimagetool-x86_64.AppImage"
if [ ! -f "$APPIMAGETOOL" ]; then
    echo "6. Descargando appimagetool..."
    APPIMAGETOOL_URL="https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage"
    if command -v wget &> /dev/null; then
        wget -q "$APPIMAGETOOL_URL" -O "$APPIMAGETOOL"
    elif command -v curl &> /dev/null; then
        curl -L -o "$APPIMAGETOOL" "$APPIMAGETOOL_URL"
    else
        echo "Error: Se necesita wget o curl para descargar appimagetool"
        exit 1
    fi
    chmod +x "$APPIMAGETOOL"
    echo "   appimagetool descargado"
else
    echo "6. Usando appimagetool existente..."
fi
echo ""

# Crear AppImage
echo "7. Creando AppImage..."
APPIMAGE_NAME="IEVRModManager-${VERSION}-x86_64.AppImage"
ARCH=x86_64 "$APPIMAGETOOL" "$APPIMAGE_DIR" "$APPIMAGE_NAME" 2>&1 | grep -v "warning\|info" || true

if [ ! -f "$APPIMAGE_NAME" ]; then
    echo "Error: No se pudo crear el AppImage"
    exit 1
fi

chmod +x "$APPIMAGE_NAME"
APPIMAGE_SIZE=$(du -h "$APPIMAGE_NAME" | cut -f1)
echo "   AppImage creado: $APPIMAGE_NAME ($APPIMAGE_SIZE)"
echo ""

# Crear carpeta de release
RELEASE_DIR="release-linux"
if [ -d "$RELEASE_DIR" ]; then
    rm -rf "$RELEASE_DIR"
fi
mkdir -p "$RELEASE_DIR"

# Copiar AppImage a carpeta de release
cp "$APPIMAGE_NAME" "$RELEASE_DIR/"
echo "8. AppImage copiado a carpeta 'release-linux'"
echo ""

# Mostrar resumen
echo "=== Resumen ==="
echo "Versión: $VERSION"
echo "Tag sugerido: v$VERSION"
echo "AppImage: $RELEASE_DIR/$APPIMAGE_NAME"
echo ""
echo "Próximos pasos:"
echo "1. Prueba el AppImage: ./$RELEASE_DIR/$APPIMAGE_NAME"
echo "2. Si funciona correctamente, súbelo a GitHub Releases"
echo "3. Crea un tag: git tag v$VERSION"
echo "4. Publica el release en GitHub"
echo ""

echo "¡Listo! El AppImage está en la carpeta 'release-linux'"
