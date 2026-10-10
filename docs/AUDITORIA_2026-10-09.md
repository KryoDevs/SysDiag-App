# Auditoría 2 de SysDiag — 2026-10-09

**Rama:** `arena/07f995ef-sysdiag-app` · **Base:** `05bdcde` (`main`) · Continúa la [auditoría 1](AUDITORIA.md).

Esta pasada buscó defectos que la auditoría anterior no había señalado: datos engañosos, estados de error mal
clasificados, textos que contradicen el comportamiento real, crecimiento sin límite y higiene del repositorio.
Cada hallazgo tiene tratamiento en esta rama o una decisión explícita en la sección de decisiones.

## Método

- **Lectura completa** del motor de diagnóstico, servicios, módulos de `Core/` (hardware, red, almacenamiento,
  seguridad, drivers, Windows, diagnóstico), ViewModel, ventanas, pruebas, CI, instalador y scripts.
- **Comprobaciones mecánicas** (el sandbox es Linux y no tiene SDK de .NET ni WPF):
  - Claves de recursos XAML usadas frente a las definidas: 66 usadas, todas definidas.
  - Bindings XAML frente a miembros públicos: sin rupturas.
  - Etiquetas del menú frente a los casos de navegación: 18 de 18 manejadas.
  - IDs de reparación usados frente al catálogo: todos existen; cuatro del catálogo no están enlazados a hallazgos
    (ver decisiones).
  - Sintaxis C# de los 82 archivos con un parser independiente, antes de cada push. Quedan 5 avisos conocidos de la
    gramática en `WmiHelper.cs` (`row?[property]`), que es C# válido.
- **Compilación y pruebas** solo en CI de Windows, que es el único entorno donde WPF y WMI existen.

## Hallazgos y tratamiento

| ID | Sev. | Hallazgo | Tratamiento |
|---|---|---|---|
| A01 | Alta | Térmicas: una zona ACPI sin lectura (valor 0) se convertía en −273,15 °C y se mostraba como «0 °C», incluida la tarjeta de temperatura. | `ConvertirTemperaturaAcpi` acepta solo lecturas plausibles; sin lectura, «no publicada por el firmware». Pruebas. |
| A02 | Alta | Wi-Fi: señal y canal se asignaban por orden de línea. Si netsh imprime el canal antes que la señal, un punto de acceso hereda la señal del anterior. | Parser por BSSID (`ParseRedesCercanas`), independiente del orden. Pruebas con ambos órdenes y con claves en español. |
| A03 | Media | Tendencia del historial: mezclaba diagnósticos parciales (p. ej. solo «Limpieza») con completos, y dibujaba saltos que no miden lo mismo. | `Historial(modulos:)` usa la misma cobertura que la comparación. Prueba. |
| A04 | Media | winget sin actualizaciones pero con código de salida distinto de cero se reportaba como «No se pudo consultar». | Se reconoce el mensaje de catálogo vacío antes que el código de salida. Pruebas. |
| A05 | Media | Drivers: si un origen fallaba y otro respondía «sin novedades», se mostraba un error con la acción de reparar Windows Update. | Se distingue «Windows respondió». |
| A06 | Media | WHEA: hasta 50.000 filas por diagnóstico (≈10 MB en JSON). El historial, de hasta 500 entradas, se relee completo en cada archivado. Justo cuando el hardware falla. | Se guardan hasta 2.000 filas. Los conteos son un límite inferior y el aviso lo declara. |
| A07 | Media | Limpieza cancelada a mitad de una fila: la tabla seguía listando archivos ya borrados. | `try/finally`: la fila refleja el disco. |
| A08 | Media | Reparar Windows Update deja la caché renombrada (varios GB) en `C:\Windows` sin indicar dónde ni que puede borrarse. | Mensaje con nombre y ubicación, y recomendación de borrado manual. |
| A09 | Media | Instalación de drivers: acepta licencias (`AcceptEula`) sin informarlo en el diálogo de confirmación. | Aviso explícito antes de confirmar. |
| A10 | Baja | Mensajes de punto de restauración sugerían «continuar sin él», pero todos los llamadores abortan sin punto. | Textos alineados con el comportamiento real. |
| A11 | Baja | Estado de ejecución mal redactado («quedó falló o incompleto»). | Redacción corregida por estado. |
| A12 | Baja | Limpieza: si ninguna categoría existía, pulsar «Limpiar» no mostraba nada. | Aviso explícito. |
| A13 | Baja | Menú: pulsar un módulo durante otra operación lo dejaba marcado, y no volvía a dispararse. | Se libera la selección y se registra en el log. |
| A14 | Baja | Seis `Process` lanzados desde la interfaz no se liberaban (handles hasta el GC). | `?.Dispose()`. |
| A15 | Baja | Código muerto: `ReadWlanAutoconfig` y `SaveState` sin llamadores; un `using` sin uso. | Eliminados. |
| A16 | Baja | Servicios: la columna «Descripción» mostraba el nombre interno. | Encabezado «Nombre interno». |
| A17 | Baja | Historial: no se distinguía un diagnóstico completo de uno parcial. | Columna «diagnóstico completo» o «parcial · n/10 módulos». |
| A18 | Baja | Frase de arranque con error gramatical («no reconozcas necesitar»). | Corregida. |
| A19 | Baja | README con estructura y módulos desactualizados. | Actualizado. |
| A20 | Baja | Repositorio: 116 MB de binarios versionados (`sysdiag-1.0.0.zip` 66 MB, `installer/Output/sysdiag-1.0.0.exe` 50 MB) pese a `.gitignore`. | Dejan de versionarse. El historial no se reescribe. |
| A21 | Baja | Umbral de pruebas de CI (80) muy por debajo de la suite (101 en la auditoría 1). | Umbral sube a 114 (ratchet). |
| A22 | Media | El inventario se cachea 3 minutos y también incluía el espacio libre de los discos: un diagnóstico hecho tras limpiar mostraba el valor anterior y su aviso. | Los discos se leen en cada corrida; solo se cachea el inventario estático (`LeerDiscos`). |
| A23 | Media | Historial: cada corrida se archivaba con la cobertura acumulada del reporte fusionado. Un «Red» suelto hecho después de un diagnóstico completo se etiquetaba «diagnóstico completo» y se comparaba como tal. | Se archiva una copia (`ParaArchivo`) que declara solo los módulos medidos en esa corrida. Prueba. |
| A24 | Baja | La ventana de historial leía todos los diagnósticos archivados en el hilo de la interfaz: con 500 entradas, la ventana tardaba en abrirse. | La lectura se hace en segundo plano (`Task.Run`) y las filas se construyen en el hilo de UI. |
| A25 | Media | Ninguna ventana se construía en CI: un error de XAML o de code-behind solo aparecía al abrirla. | El autotest del EXE publicado construye las ocho ventanas (sin mostrarlas) antes de declarar OK. |

## Decisiones (revisadas, no cambiadas)

- **Catálogo de reparaciones:** `flush-dns`, `wifi-power`, `plan-energia` y `wlan-autoconfig` no están enlazados a
  ningún hallazgo. Siguen disponibles en *Optimizar*. Enlazar «plan de energía» a un aviso de throttling térmico
  podría empeorar la temperatura, así que queda como decisión de producto.
- **Restaurar con un plan eliminado:** la restauración falla completa si un plan respaldado ya no existe. Es la
  decisión de la auditoría 1 (no restaurar a medias). El costo es que el respaldo queda pendiente; una alternativa
  sería omitir ese plan con aviso.
- **Análisis de limpieza suelto:** se archiva en el historial como diagnóstico parcial. Con el filtro por cobertura
  ya no contamina la tendencia, pero sí aparece en la lista (ahora con su cobertura visible).
- **Exportaciones** (informes, JSON, CSV): no tienen rotación porque las genera el usuario al pedirlas.
- **Windows Update:** las llamadas COM síncronas siguen sin ser cancelables. Está documentado en la auditoría 1.

## Verificación

- **Sintaxis:** los 82 archivos C# se revisaron con un parser independiente antes de cada push. Sin errores nuevos.
- **Compilación, pruebas y gates en Windows (CI):** cada lote se publicó en la rama y CI corrió sobre ese commit.

| Commit | Contenido | *Compilar y probar* (run) | Pruebas | *Validate fixture* (run) |
|---|---|---|---|---|
| `6a17dad` | Lote 1: correcciones de datos y estados | `37896391491` ✅ | 113/113 | `37896391453` ✅ |
| `0ddac48` | Lote 2: textos, Process, WHEA, dead code, umbral | `37896866226` ✅ | 113/113 | — |
| `8c4fd7d` | Lote 3: espacio libre de discos sin caché | `37897164372` ✅ | 113/113 | `37897164452` ✅ |
| `ae7b420` | Lote 4: cobertura por corrida (`ParaArchivo`) | `37897507716` ✅ | 114/114 | — |
| `56df825` | Lote 5: historial en segundo plano, umbral 114 | `37897796941` ✅ | 114/114 | `37897796969` ✅ (ambos jobs) |
| **`8fa11d6`** | **Lote 6, código final**: autotest construye ventanas | **`37898173399` ✅** | **114/114** | **`37898173340` ✅ (ambos jobs)** |

En el commit final, *Compilar y probar* ejecutó todos sus pasos con éxito: restauración, compilación Release,
suite de pruebas, gate de regresiones (≥114), autotest del runner compartido, publicación autocontenida y
**«Validar el paquete publicado»**, que arranca el EXE con `--self-test` y ahora construye las ocho ventanas.
*Validate fixture* también pasó en sus dos jobs, incluidas las pruebas de integración en Debug.

Limitación del sandbox: los logs completos no se descargan desde aquí (el host de resultados no está en la lista
permitida). La evidencia se toma de las conclusiones por paso y de las anotaciones de CI vía API.

## Pendientes que CI no puede verificar

- Interacción de la GUI en Windows 10/11: menú durante operaciones, cancelación, ventanas nuevas y textos.
- Restauración real de planes de energía y DNS sobre una VM descartable (ya pendiente en la auditoría 1).
- Salida real de `netsh` en Windows en español para el escaneo Wi-Fi. El parser se basa en el mapa de claves
  localizadas ya existente, y conviene confirmarlo en un equipo real.
- Matriz de hardware: Modern Standby, GPU híbrida, SMART y sensores térmicos.
- Firma de código del EXE y prueba real del instalador Inno Setup: siguen fuera de CI (pendiente desde la auditoría 1).
- Aceptación de licencias de drivers (A09): hoy se acepta al confirmar, con aviso. Conviene decidir si debe pedirse
  licencia por paquete.

---

## Correcciones posteriores al informe

- **A25 decía «las ocho ventanas»; son diez.** El `--self-test` construye hoy
  `MainWindow`, `HistoryWindow`, `SettingsWindow`, `ProfilesWindow`, `OptimizeWindow`,
  `CleanupWindow`, `PingMonitorWindow`, `DialogWindow`, `ActivationWindow` y
  `TweaksWindow`. Al momento de la auditoría el número era correcto: la cuenta subió
  con las ventanas propias del rediseño. Se anota acá en lugar de editar la tabla para
  que el informe siga diciendo lo que se vio entonces.
