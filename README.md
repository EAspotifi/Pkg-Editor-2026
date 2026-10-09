# Pkg Editor — Linux (portable)

Editor gráfico para paquetes **PKG**, imágenes **PFS** y proyectos **GP4** de PlayStation 4, basado en el ecosistema [LibOrbisPkg](https://github.com/maxton/LibOrbisPkg).

---

## Créditos y alcance de este repositorio

| Rol | Quién |
|-----|--------|
| **Autor original de esta línea del editor (Pkg-Editor-2023)** | [**KimieStar**](https://github.com/KimieStar) — fork y mejoras sobre el proyecto de la comunidad OpenOrbis / maxton ([repositorio original](https://github.com/KimieStar/Pkg-Editor-2023)). |
| **Biblioteca core (PKG / PFS / GP4 / SFO)** | [maxton / LibOrbisPkg](https://github.com/maxton/LibOrbisPkg) |
| **Port Linux (este trabajo)** | **EAspotifi** — únicamente la **versión nativa para Linux**, empaquetada como **ejecutable portable** (sin instalador ni .NET en el sistema). |

No reclamamos la autoría del editor Windows ni de la lógica de LibOrbisPkg. Nuestro aporte es hacer usable **Pkg Editor en Linux** con interfaz **Avalonia**, rutas y configuración adaptadas a Linux, y un binario **self-contained** listo para doble clic.

---

## Qué incluye la versión Linux

- **`PkgEditorLinux/`** — aplicación de escritorio (.NET 8 + Avalonia UI), tema claro minimalista (celeste / PS4-inspired).
- **`LibOrbisPkg/`** — biblioteca compartida (actualizada a .NET 8; ajustes de rutas GP4 y KeyDB en `~/.config/LibOrbisPkg/keydb.json`).
- **`Portable/`** — build **linux-x64** en un solo ejecutable (~43 MB), sin instalar runtime.
- **`PkgEditor/`** — código original Windows Forms (referencia / paridad); no es el objetivo de uso en Linux.

Funcionalidad principal en Linux (según el estado del port):

- Abrir **`.pkg`**, **`.pfs`**, **`.dat`**, **`.gp4`**, **`.sfo`**
- Vista PKG: metadatos, **portada** (`icon0.png`), cabeceras (ObjectTree), **PARAM.SFO**, explorador **PFS**
- **Export to GP4 Project** (elige carpeta de destino + barra de progreso; genera `Project.gp4` y archivos)
- Entradas del PKG: listado, extract / extract & decrypt
- Desbloqueo de PKG / PFS cifrados (passcode / EKPFS / XTS + KeyDB)
- Proyectos **GP4**: edición básica de metadatos, **Build PKG / Build PFS** con validación
- Menús: nuevo GP4/SFO, guardar, combinar partes PKG, arrastrar y soltar archivos
- Créditos en **Help → About**: original Maxton / línea KimieStar · port Linux EAspotifi

---

## Uso rápido — Portable (recomendado)

1. Entra en la carpeta [`Portable/`](Portable/).
2. Ejecuta **`PkgEditor`** (doble clic o `./PkgEditor`).
3. Si hace falta, usa **`Launch.sh`** o marca el archivo como ejecutable.

Detalle en [Portable/README.md](Portable/README.md).

Requisitos: **GNU/Linux x64** (Fedora, Ubuntu, etc.). No hace falta instalar .NET.

Las claves guardadas van a: `~/.config/LibOrbisPkg/keydb.json`

---

## Desarrollo — compilar desde código

Requisitos: [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
cd PkgEditorLinux
dotnet run
```

Release local:

```bash
dotnet build -c Release
```

---

## Regenerar el portable

Desde la raíz del repositorio:

```bash
./scripts/publish-portable.sh
```

Genera de nuevo `Portable/PkgEditor` (single-file, self-contained, `linux-x64`).

---

## Estructura del proyecto

```
Pkg-Editor-2023/
├── LibOrbisPkg/          # Core PKG/PFS/GP4/SFO
├── PkgEditor/            # Editor Windows (original)
├── PkgEditorLinux/       # Editor Linux (Avalonia)
├── Portable/             # Ejecutable portable listo para usar
└── scripts/
    └── publish-portable.sh
```

---

## Licencia

Este proyecto hereda la licencia del upstream (**GNU LGPL v3** — ver [LICENSE.txt](LICENSE.txt)). Respeta los términos de LibOrbisPkg y del fork de KimieStar al redistribuir.

---

## Enlaces

- Autor del fork Pkg-Editor-2023: [github.com/KimieStar](https://github.com/KimieStar)
- LibOrbisPkg: [github.com/maxton/LibOrbisPkg](https://github.com/maxton/LibOrbisPkg)
