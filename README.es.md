# Virtual Desk Manager

[English](README.md) · **Español**

Gestor de escritorios virtuales y ventanas para **Windows 11**. Pone nombre a tus escritorios y los
recupera si Windows los pierde, lleva cada aplicación al escritorio donde la quieres, devuelve las
ventanas a su sitio al reconectar las pantallas, reparte las ventanas en zonas y te da atajos de
teclado para todo, además de una consola que baja desde arriba de la pantalla.

Funciona como usuario normal: **no pide permisos de administrador, no instala drivers y no se mete
dentro de otros programas**.

Las descargas están en [Releases](../../releases).

---

## Descargar

| Fichero | Para qué |
|---|---|
| `VirtualDeskManager-<versión>.msi` | **El instalador, el recomendado.** Se instala sólo para tu usuario, sin pedir administrador, y se actualiza solo. |
| `VirtualDeskManager-<versión>.exe` | Versión portable: un único fichero que se ejecuta sin instalar. No se actualiza solo. |

Los dos llevan .NET dentro: **no hace falta instalar nada más**.

- **Versiones estables**: sin etapa en el nombre (`1.0.0`). Son las recomendadas.
- **Betas**: con la etapa en el nombre (`1.0.0-beta.4`), marcadas como *Pre-release*. Sirven para
  probar lo nuevo antes de que sea estable y pueden tener fallos.

### Aviso de Windows al abrirlo

Por ahora los ejecutables **no llevan un certificado reconocido por Windows**; estamos trabajando
en ello. Mientras tanto puede salir:

- **SmartScreen** («Windows protegió su PC»): pulsa **Más información** y luego **Ejecutar de todas
  formas**.
- **Smart App Control**, si lo tienes activado, puede bloquearlo sin ofrecer la opción de
  ejecutarlo. En ese caso hay que esperar a la versión firmada, o compilarlo tú (ver abajo).

---

## Requisitos

- **Windows 11 de 64 bits.** En Windows 10 no está probado.
- Una cuenta de usuario normal. No hace falta ser administrador.

---

## Primeros pasos

1. Instala el `.msi`, o abre el `.exe` portable.
2. Virtual Desk Manager se queda en la **bandeja del sistema**, junto al reloj. El icono muestra el
   número del escritorio en el que estás.
3. **Un clic en el icono** abre la ventana principal:
   - **Mis escritorios**: una tarjeta por escritorio con sus ventanas en miniatura. Clic para ir,
     arrastra una ventana para llevarla a otro, y desde el menú «…» cambias el nombre o eliminas
     el escritorio. La tarjeta «+» crea uno nuevo.
   - **Automatizaciones**: «cuando se abra *tal aplicación*, llévala a *tal escritorio*»,
     maximizada, siempre visible o en todos los escritorios.
   - **Distribuir ventanas**: zonas para encajar las ventanas en cada pantalla.
   - **Ajustes**: todo lo demás, con buscador (**Ctrl+F**) y explicación de cada opción (**F1**).
4. La ventana que tienes delante muestra un **botón propio en su barra de título**, junto a
   minimizar: siempre visible, en todos los escritorios, mover a otro escritorio o «abrir siempre
   esta aplicación aquí».

Cerrar la ventana no cierra Virtual Desk Manager: sigue en la bandeja. Para salir, usa el menú del
icono de la bandeja.

---

## Qué hace

- **Escritorios con nombre que no se pierden.** Si Windows pierde tus escritorios, se reponen con
  sus nombres al arrancar.
- **Crear y eliminar escritorios** desde *Mis escritorios* o desde la vista de todos los
  escritorios. Eliminar no cierra ninguna ventana: pasan al escritorio de al lado.
- **Automatizaciones** para que cada aplicación se abra donde quieres. Se aplican una vez, cuando
  aparece la ventana.
- **Zonas** por pantalla: arrastra una ventana con **Mayús** pulsada para encajarla, sin rendijas.
- **Recuerda dónde estaba cada ventana** y la devuelve a su sitio al volver del reposo o al
  conectar otra vez una pantalla.
- **Vista de todos los escritorios** con miniaturas en vivo: cambia de escritorio, mueve ventanas
  arrastrándolas, ciérralas, y crea o elimina escritorios.
- **Consola desplegable** al estilo de la de Quake, con el intérprete que elijas y una chincheta
  para dejarla fija.
- **Marcas en la barra de tareas** con el escritorio de cada aplicación.
- **Copias de seguridad** de la configuración: automáticas, en OneDrive o Google Drive (como
  carpeta) o en un fichero.
- **Actualizaciones** desde la propia aplicación, con la firma de cada versión comprobada antes de
  instalar. Nunca instala nada sin preguntar, salvo que se lo pidas.
- **10 idiomas**: español, inglés, francés, portugués, ruso, chino, hindi, bengalí, urdu y árabe.

### Atajos de serie

Todos se cambian en **Ajustes › Atajos de teclado**.

| Atajo | Qué hace |
|---|---|
| `Ctrl + Alt + ←` / `→` | Escritorio anterior / siguiente |
| `Ctrl + Shift + 1…9` | Ir directo a ese escritorio |
| `Ctrl + Alt + Shift + ←` / `→` | Llevar la ventana activa al escritorio anterior / siguiente |
| `Ctrl + Alt + ↑` | Vista de todos los escritorios |
| `Win + Ctrl + ↑` / `↓` | Abrir / cerrar la vista de tareas de Windows |
| `Ctrl + Alt + T` | Ventana activa siempre visible |
| `Ctrl + Alt + P` | Ventana activa en todos los escritorios |
| `Ctrl + Shift + º` | Bajar / recoger la consola (la tecla de debajo de Escape) |
| `Ctrl + Alt + Shift + º` | Fijar / soltar la consola |

---

## A tener en cuenta

- **Atajos que ya usa otro programa.** Un atajo global gana al de la aplicación (por ejemplo,
  `Ctrl + Shift + número` también lo usa Excel). Si un atajo sale en rojo, otro programa lo tiene
  ocupado: cámbialo.
- **Ventanas de administrador.** Sobre una ventana abierta «como administrador» no se puede actuar
  sin serlo, y Virtual Desk Manager no lo pide nunca.
- **Actualizaciones de Windows.** Mover ventanas entre escritorios usa una parte interna de
  Windows. Si una actualización la cambia, la aplicación sigue funcionando en un modo básico y
  avisa de lo que queda desactivado.
- **Algunas aplicaciones** (Teams, por ejemplo) dibujan su propio borde, y entre dos ventanas
  encajadas puede quedar una raya de un píxel que no se puede quitar desde fuera.

La configuración está en `%LOCALAPPDATA%\VirtualDeskManager` (`settings.json`, `hotkeys.json`,
`rules.json` y los registros en `logs\`). **Ajustes › Herramientas › Diario** cuenta en directo lo
que va pasando, por si algo no funciona.

---

## Compilar

Hace falta el **SDK de .NET 10** en Windows.

```powershell
dotnet build
dotnet test
dotnet run --project src\VirtualDeskManager.App
```

Para generar el instalador y el portable, con el runtime dentro:

```powershell
dotnet tool install --global wix --version 5.0.2
wix extension add --global WixToolset.UI.wixext/5.0.2
wix extension add --global WixToolset.Util.wixext/5.0.2

.\installer\build.ps1              # el .msi
.\installer\build.ps1 -Portable    # el .exe
```

Lo generado queda en `dist\`.

### Cómo está hecho

Tres proyectos, con una regla clara entre ellos:

- **`VirtualDeskManager.Core`**: la lógica, sin Win32 ni COM, y por eso con tests
  (`tests/VirtualDeskManager.Core.Tests`).
- **`VirtualDeskManager.Windows`**: todo el P/Invoke y el COM de Windows.
- **`VirtualDeskManager.App`**: la aplicación WPF.

Más detalle en [docs/arquitectura.md](docs/arquitectura.md).

---

## Licencia

**[PolyForm Noncommercial 1.0.0](LICENSE.md)**. Gratis para cualquier uso **no comercial**:
particulares, estudio, proyectos personales, ONG, centros educativos y administraciones públicas,
con derecho a modificarlo y redistribuirlo.

- **Usarlo en una empresa cuenta como uso comercial**, aunque no se venda ni se cobre por él. Para
  eso hace falta un permiso aparte del autor.
- No es software libre en el sentido de la OSI (es *source available*). Por eso GitHub la muestra
  como «Other».

Copyright © 2026 Durillo.
