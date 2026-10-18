# Arquitectura de Virtual Desk Manager

Cómo está hecho Virtual Desk Manager por dentro: qué hay en cada proyecto y cada carpeta, por dónde viaja cada
cosa y qué reglas no se rompen. Para quien vaya a tocar el código. Lo que hace cada clase está en
su propia documentación (comentarios `///`); esto es el mapa.

Escrito el 2026-10-09, sobre la rama `deuda-tecnica`. La reorganización del código (BACKLOG 3.6)
lo cambiará: al mover carpetas, actualizar este documento en el mismo commit.

El 2026-10-11 (1.0.0) se separó el enlace entre equipos, que ahora es el proyecto MiRed: aquí ya
no hay `Core/Link`, `Windows/Link`, `Windows/Share`, `Windows/Browser` ni `tools/LinkLab`.

## Los tres proyectos

```
VirtualDeskManager.Core      Lógica pura: modelos, comandos, reglas, configuración.
                             Sin Win32 ni COM. Todo lo que se puede probar sin Windows.
VirtualDeskManager.Windows   Todo el P/Invoke y el COM: ventanas, escritorios virtuales,
                             atajos, avisos de ventanas, barra de tareas, consola.
VirtualDeskManager.App       La aplicación WPF: ventanas, bandeja, y AppHost, que monta
                             los servicios y los conecta.
tests/…Core.Tests            Pruebas de Core, con dobles de los servicios de Windows (Fakes.cs).
tools/                      LogoGenerator, Capturas (fotos de los ajustes) y Publicar.
```

Core no depende de nadie. Windows depende de Core. App depende de los dos. Las pruebas sólo de
Core: lo que necesita Windows de verdad se prueba a mano o con un banco de pruebas que no toque
la entrada del usuario.

## Las reglas que no se rompen (§47 de la especificación)

1. La interfaz nunca llama a Win32: sólo habla con `AppHost` y sus servicios.
2. Los atajos generan un `CommandDefinition`; no llaman a implementaciones.
3. Las reglas generan acciones que se aplican a través de servicios.
4. Los comandos usan servicios, nunca P/Invoke.
5. El COM de los escritorios virtuales está encerrado en `Windows/VirtualDesktops`.
6. Toda función avanzada declara si está disponible en `VirtualDesktopCapabilities`; si no lo
   está, la interfaz la enseña desactivada y dice por qué (§14). Nunca una excepción.
7. Un fallo sobre una ventana ajena devuelve un `OperationResult`; nunca tumba la aplicación.
8. Sin sondeo: los cambios de ventanas llegan por `SetWinEventHook` a una única cola.

Y una más, de §36: **la aplicación funciona como usuario normal.** No pide administrador, no
instala drivers, no inyecta DLL.

## Por dónde viaja cada cosa

### Un atajo de teclado

```
Windows/Hotkeys/HotkeyManager (RegisterHotKey, en el hilo de mensajes)
  → HotkeyTriggeredEventArgs con un CommandDefinition
  → Core/Commands/CommandDispatcher
  → el ICommandHandler de ese tipo (DesktopCommandHandler, ZoneCommandHandler…)
  → servicios de Core/Abstractions (IWindowManager, IVirtualDesktopService…)
  → su implementación en Windows (Win32WindowManager, …VirtualDesktopService)
```

Los menús (el botón de la barra de título, la rejilla de escritorios) entran por el mismo sitio:
montan un `CommandDefinition` y llaman a `AppHost.ExecuteAsync`.

### Una ventana que aparece

```
Windows/Events/WinEventWindowEventService (SetWinEventHook)
  → el aviso sólo deja el HWND en una cola acotada (el callback nativo no hace nada más)
  → un hilo aparte quita repetidos, lee la ventana y lanza WindowShown / WindowCreated…
  → Core/Rules/RuleApplicationService → RuleEngine (qué reglas encajan)
  → WindowActionGuard (sin bucles: ni reentrada por HWND, ni repetir lo mismo en 2 s)
  → las acciones, a través de los servicios
```

Windows casi siempre avisa de una ventana nueva como *mostrada* y no como *creada*: al crearse aún
no tiene título y el filtro la descarta. Las pruebas simulan *mostrada* (BACKLOG 3.1).

### El hilo de mensajes

`Windows/Messaging/Win32MessageLoop` tiene su propio hilo STA con una ventana sólo de mensajes.
Ahí viven `RegisterHotKey` y `SetWinEventHook`, que exigen un bucle de mensajes y liberarse desde
el mismo hilo. El hilo de la interfaz de WPF nunca se bloquea, y todo sigue funcionando con la
ventana de ajustes cerrada.

### Los escritorios virtuales

Hay dos implementaciones, y `VirtualDesktopServiceFactory` las monta al arrancar:

- **`PublicVirtualDesktopService`**: sólo lo que Windows documenta. Poco, pero estable.
- **`InternalVirtualDesktopService`**: se apoya en la anterior y añade lo que dan las interfaces
  COM internas de Windows (`InternalInterfaces`), que cambian entre versiones: crear, renombrar,
  mover ventanas, fijar. Si esta versión de Windows no las tiene como se esperan, queda sólo la
  pública.

Lo que cada una puede hacer se declara en `VirtualDesktopCapabilities` (regla 6). Si Windows pierde
el conjunto de escritorios, las reglas que nombran uno se resuelven otra vez por nombre
(`DesktopResolver`).

## Dónde está cada cosa

### Core

| Carpeta | Qué hay |
|---|---|
| `Abstractions` | Las interfaces de los servicios que implementa Windows (`IWindowManager`, `IVirtualDesktopService`, `IHotkeyManager`…). |
| `Backup` | Copias de la configuración (`ConfigurationBackupService`), también a carpetas de la nube. |
| `Commands` | `CommandDispatcher` y un `ICommandHandler` por familia de comandos. |
| `Configuration` | `AppSettings` y sus partes, la lectura y escritura en JSON, los valores de fábrica y las migraciones. |
| `Desktops` | Cuentas de la rejilla de escritorios y de la consola desplegable. |
| `Diagnostics` | El diario: `FileAppLogger` (un fichero por día y memoria para la pestaña Diario) y los nombres de eventos. |
| `Events` | Los argumentos de los avisos (atajo pulsado, ventana detectada). |
| `Layout` | Recordar y devolver la posición de las ventanas por pantalla. |
| `Localization` | Los 10 idiomas (`Strings/*.json`) y `Loc`. |
| `Models` | Datos que van de un lado a otro: `WindowInfo`, `CommandDefinition`, `OperationResult`… |
| `Rules` | El motor de reglas y su aplicación a las ventanas. |
| `Taskbar` | Los distintivos de la barra de tareas. |
| `Zones` | Las zonas para encajar ventanas. |

### Windows

| Carpeta | Qué hay |
|---|---|
| `Native` | Las declaraciones P/Invoke, constantes y estructuras de Win32. Nada más. |
| `Windows` | `Win32WindowManager`: leer y manejar ventanas. |
| `VirtualDesktops` | Las dos implementaciones de escritorios virtuales y su COM. |
| `Events` | Los avisos de ventanas (`SetWinEventHook`). |
| `Messaging` | El hilo de mensajes. |
| `Hotkeys` | Los atajos y la captura de teclas para el editor de atajos. |
| `Navigation` | Moverse entre escritorios y la Vista de tareas. |
| `Overlay` | Seguir la ventana activa (botón de la barra de título) y las miniaturas de DWM. |
| `Taskbar` | Encontrar los botones de la barra de tareas (UI Automation) para los distintivos. |
| `Zones` | Seguir el arrastre de una ventana para encajarla. |
| `Console` | La consola desplegable. |
| `Startup` | Arrancar con Windows. |

### App

| Dónde | Qué hay |
|---|---|
| `App.xaml.cs` | El arranque: crea `AppHost`, la bandeja, las ventanas superpuestas y los diálogos. |
| `AppHost.cs` | Monta todos los servicios y los conecta. Es lo único que la interfaz conoce (regla 1). |
| raíz | Los servicios de la aplicación: la rejilla, la consola, los distintivos, recordar ventanas, zonas, actualizaciones… |
| `Views/Enlace/VentanaVdesk` | La ventana principal (estilo macOS): el grupo ESCRITORIOS (Mis escritorios, Automatizaciones, Distribuir ventanas) y los Ajustes dentro. El nombre de la carpeta es de cuando había enlace. |
| `Views/Settings/PanelAjustes` | Los ajustes: barra lateral, buscador y ayuda con F1. Sólo navega; las páginas están en `Views/Settings`. |
| `Views/Settings` | Una página por sección de los ajustes (Inicio, Mis escritorios…), la fila de ajuste (`SettingRow`, `SettingCard`) y su estilo (`SettingsStyles.xaml`). El estilo lo fija `docs/ajustes/rediseno.html`. |
| `ViewModels` | Los modelos de vista. `MainViewModel` lleva casi todas las secciones; el diario y la ventana principal tienen el suyo. |
| `Views` | Las demás ventanas: rejilla, editores, copias… |
| `Overlay` | El botón de la barra de título, los distintivos y el menú de cada ventana. |
| `Localization` | La extensión `{l:T Clave}` de XAML y los convertidores. |
| `Tray` | El icono de la bandeja. |

## La documentación del código

Todo lo público lleva su comentario `///` (completado el 2026-10-09), y los tres proyectos
compilan con el aviso CS1591 encendido: lo que entre sin documentar se ve en cada compilación. El
estilo: decir para qué sirve y por qué es así, no repetir el nombre; y las trampas medidas, con su
fecha, que es lo que más vale dentro de unos meses.

## Desviaciones deliberadas de la especificación

| Especificación | Implementación | Motivo |
|---|---|---|
| `HotkeyDefinition.Key` de tipo WPF `Key` | `HotkeyKey` propio con los códigos VK | Meter WPF en Core rompería la regla 1 |
| `IWindowManager` devolviendo `bool` | Devuelve `OperationResult` | §19 y §35: hay que distinguir `AccessDenied` de `Failed` |
| `IDesktopNavigationService` devolviendo `Task` | `Task<OperationResult>` | Mismo motivo |
| `CommandType` sin valor por defecto | Se añade `None = 0` | Un JSON con un comando ausente no debe convertirse en otro comando |
| Framework de logging | Logger propio en fichero | "Sin dependencias pesadas": cero paquetes NuGet en `src/` |

## Limitaciones conocidas

- Entre dos ventanas encajadas en zonas contiguas, **algunas aplicaciones siguen dibujando una raya
  clara de un píxel**, y ésa no se puede quitar. Virtual Desk Manager quita las tres que pone Windows —los siete
  píxeles transparentes de agarre, el redondeo de esquinas y el filo de un píxel del borde—, pero
  lo que la aplicación pinte dentro de su propia área de cliente es suya.

  Medido con Teams: tras quitarle todo lo de Windows, su columna izquierda sigue en `#6A6A6A` sobre
  un contenido de `#060606`. Y **aparece igual en su borde derecho contra el borde de la pantalla**,
  donde no hay ninguna ventana al lado, lo cual demuestra que no es separación sino su propio
  marco. Tocarlo exigiría inyectar un DLL en el proceso ajeno, que es justo lo que la sección 36
  prohíbe.

  No todas lo hacen: entre Visual Studio Code y una ventana de Edge la junta no deja ni un píxel.

- **Crear**, **renombrar** y **eliminar** escritorios no van por la vtable: sus métodos caen en el
  tramo cuyo orden cambia entre compilaciones de Windows, y acertar la ranura equivocada se lleva
  por delante al Explorador. Crear usa el atajo documentado `Ctrl+Win+D`, renombrar escribe donde
  el propio Windows guarda el nombre (los dos comprobados contra la compilación 26200) y eliminar
  (desde la 1.0.0-beta.4) va al escritorio, comprueba que de verdad está delante, pulsa
  `Ctrl+Win+F4` y confirma en el registro que ha desaparecido; las ventanas pasan al de al lado.
- Fijar una **aplicación** entera (todas sus ventanas) tampoco: necesita el identificador de
  modelo de aplicación de la vista, cuya posición no está verificada. Fijar una **ventana** sí.
- La navegación izquierda/derecha usa el backend avanzado cuando está disponible. Si no lo está,
  sintetiza el atajo del shell con `SendInput` (§15 lo permite: el mecanismo queda oculto tras la
  interfaz), liberando antes Alt y Shift porque al venir de `Ctrl+Alt+Flecha` siguen pulsadas.
- Sobre ventanas de procesos elevados no se puede actuar sin elevación; se devuelve
  `AccessDenied` y no se pide UAC (§36).
- Si una actualización de Windows cambia las interfaces internas, la aplicación seguirá
  arrancando: se queda en el backend público y las funciones avanzadas aparecen deshabilitadas.
  Para volver a habilitarlas habría que añadir el nuevo identificador a la lista de candidatos.
