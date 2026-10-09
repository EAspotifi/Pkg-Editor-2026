# Pkg Editor — Portable (Linux x64)

Ejecutable autónomo: **no requiere instalar .NET** ni dependencias del proyecto.

## Uso

1. Abre esta carpeta en el explorador de archivos.
2. Doble clic en `PkgEditor`  
   (si el sistema lo pide, marca “Permitir ejecutar como programa” / “Trust and Launch”).
3. O desde terminal:

```bash
./PkgEditor
```

## Notas

- Arquitectura: **linux-x64** (Fedora, Ubuntu, etc.)
- Tamaño ~43 MB (incluye runtime .NET 8 + Avalonia)
- Las claves se guardan en `~/.config/LibOrbisPkg/keydb.json`

## Regenerar este portable

Desde la raíz del repo:

```bash
./scripts/publish-portable.sh
```
