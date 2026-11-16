# Política de privacidad de Virtual Desk Manager

[English](PRIVACY.md) · **Español**

*Última actualización: 16 de noviembre de 2026.*

## En resumen

**Virtual Desk Manager no recoge, no envía y no comparte ningún dato tuyo.** No tiene cuentas de
usuario, ni telemetría, ni estadísticas de uso, ni publicidad, ni informes de errores automáticos.
Todo lo que guarda se queda en tu equipo.

## Lo que guarda en tu equipo

Para funcionar, la aplicación guarda algunos datos **sólo en tu equipo**, en la carpeta
`%LOCALAPPDATA%\VirtualDeskManager`. Nada de esto se envía a ningún sitio:

- **Tu configuración**: ajustes, atajos de teclado y automatizaciones (`settings.json`,
  `hotkeys.json`, `rules.json`).
- **Dónde estaba cada ventana**, para devolverla a su sitio al cambiar de pantallas: el nombre del
  programa, el tipo de ventana, su título y su posición.
- **Un registro de diagnóstico** (`logs\`), para averiguar por qué algo no funciona. Puede incluir
  nombres de programas y títulos de ventanas. Se guardan los últimos 7 días y se puede apagar en
  *Ajustes › Herramientas*.
- **Copias de seguridad de la configuración**. Si eliges guardarlas en OneDrive o en Google Drive,
  Virtual Desk Manager sólo deja el fichero en la carpeta que ese programa sincroniza; no se conecta
  a esos servicios. A partir de ahí, ese fichero lo trata el servicio que hayas elegido, según su
  propia política.
- Si activas **Iniciar con Windows**, una entrada en el registro de Windows de tu usuario.

## Conexiones a internet

La única conexión que hace es para **buscar y descargar actualizaciones** desde
`durillo.es/updates`:

- La petición no lleva ningún identificador ni ningún dato tuyo o de tu equipo: sólo pide los
  ficheros de la versión más reciente.
- Como en cualquier petición a una web, el servidor ve la dirección IP desde la que te conectas, y
  el proveedor de alojamiento puede conservarla un tiempo en sus registros técnicos. No se usa para
  nada más.
- Puedes apagar la búsqueda automática en *Ajustes › General › Actualizaciones*.

Si descargas la aplicación desde GitHub, esa descarga se rige por la política de privacidad de
GitHub.

## Cómo borrar tus datos

Desinstala Virtual Desk Manager desde *Configuración de Windows › Aplicaciones* (o borra el `.exe`
portable) y borra la carpeta `%LOCALAPPDATA%\VirtualDeskManager`. Si hiciste copias en OneDrive o
Google Drive, bórralas también de allí.

## Cambios en esta política

Si la aplicación cambia en algo que afecte a tu privacidad, esta página se actualizará y cambiará
la fecha de arriba.

## Contacto

Para cualquier duda, abre una incidencia en
[github.com/durillo/VirtualDeskManager4Windows](https://github.com/durillo/VirtualDeskManager4Windows/issues).
