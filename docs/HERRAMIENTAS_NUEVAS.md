# Catálogo de herramientas nuevas — SysDiag

**Fecha:** 2026-10-10 · **Base:** lote 5.9 (gráficos, mediciones, interfaz por sección,
activación de Windows) · **Actualizado:** lote 5.10.

> **Estado al 5.10.** Ya están implementados **2.7** (decodificador de
> pantallazos), **3.1 a 3.4** (ensayo, deshacer por paso, verificación
> post-cambio, restauración selectiva), **4.2** (informe redactado), **4.3**
> (Markdown) y **5.1** (paleta de comandos). El resto del catálogo sigue
> abierto y en el orden de §6. Cada entrada implementada conserva su texto
> original, que explica por qué entró; al final de cada una se agregó una nota
> con lo que finalmente se hizo.

Este documento responde a una pregunta concreta: *qué herramientas nuevas harían que
SysDiag se sintiera profesional y no un script con ventanas*. Está escrito para poder
elegir, así que cada entrada dice qué hace, de dónde sale el dato, qué riesgo tiene y
cuánto cuesta.

Complementa `docs/MEJORAS.md §4`, que ya cataloga 36 herramientas con el mismo
criterio. Lo de allá no se repite acá salvo cuando esta pasada cambió el análisis.

**Coste:** **c** = días · **m** = semanas · **a** = meses o más.
**Valor:** entre corchetes, para el usuario que ya usa la app, no para el autor.

---

## 1. Primero, el límite: lo que no entra

Una herramienta de diagnóstico corre con privilegios altos en equipos de terceros y
lee datos sensibles. Ese es su valor y también su responsabilidad. Tres categorías
quedan fuera, y conviene decirlo por escrito antes de que alguien las pida:

1. **Activación de Windows por fuera de los canales oficiales.** El módulo nuevo
   (`Core/Windows/ActivationModule.cs`) lee el estado de la licencia, instala claves
   que el usuario ya tiene, apunta a un host KMS **propio** de su organización y abre
   los canales de Microsoft. No emula servidores KMS, no inyecta licencias digitales
   (HWID/KMS38), no genera ni valida claves ajenas. Ver `docs/MEJORAS.md §8.3`.
2. **Saltarse protecciones de otros fabricantes**: desactivar Defender o el firewall
   de terceros, parchear binarios, deshabilitar el Secure Boot para instalar
   controladores sin firma, «limpiadores de registro» que borran claves que no
   entienden. Que Windows lo permita no lo vuelve una buena idea en un equipo ajeno.
3. **Telemetría remota**, aunque sea anónima. El registro local, el historial
   archivado y un informe redactable cubren la necesidad de soporte sin sacar datos
   del equipo.

Todo lo que sigue respeta ese límite: **medir, explicar y revertir**, nunca ocultar
ni forzar.

---

## 2. Herramientas de diagnóstico

### 2.1 Monitor de recursos en vivo (panel continuo) — [m / muy alto]
Un panel con CPU por núcleo, RAM, disco y red refrescándose una vez por segundo, con
la misma paleta y las mismas escalas que el resto. Hoy SysDiag mide por ráfaga: útil
para un informe, inútil para ver *qué pasa cuando abro el navegador*. Es la pieza que
más cambia la percepción de la app, porque es la que se deja abierta.
**Dato:** contadores WMI `PerfOS_*` y `PerfDisk_*`; el muestreo por diferencia que ya
hace `PerformanceModule`. **Riesgo:** una app que mide consume recursos midiendo →
refresco lento por defecto y pausa automática al perder el foco.

### 2.2 Línea de tiempo térmica — [c / alto]
Historial de temperatura y frecuencia durante una ventana configurable, con la marca
del momento en que el reloj cayó. Convierte «85 °C» en «llegó a 97 °C y bajó el reloj
12 segundos», que es la respuesta a la pregunta real: *¿me rinde o no?*
**Dato:** `MSAcpi_ThermalZoneTemperature` y `PercentProcessorPerformance`, muestreados.
**Riesgo:** no todo firmware publica la zona térmica; la ausencia tiene que decirse.

### 2.3 Salud real del disco (SMART por atributos) — [m / muy alto]
El estado binario de WMI dice «OK» hasta que dice «muerto». Los atributos que
importan (sectores reasignados, pendientes, errores de medio, desgaste, temperatura)
salen de `MSFT_StorageReliabilityInformation` en Windows 10+ sin binario externo.
«Quedan 340 de 1000 ciclos de escritura» es la medición más accionable de todas.
**Riesgo:** requiere administrador en algunos equipos; reportar «n/d» con la razón.

### 2.4 Diagnóstico de red con pérdida por salto — [m / alto]
`Traceroute` existe; falta medir pérdida y jitter **por tramo**. Localiza «la pérdida
está en el segundo salto» y evita la conclusión cómoda de siempre («es el proveedor»).
**Dato:** ICMP con TTL creciente y lotes por salto, igual que el `HopAsync` actual pero
con estadística por salto en vez de un solo valor.
**Riesgo:** tarda; necesita progreso real y poder cancelar.

### 2.5 Mapa de canales Wi-Fi — [c / alto]
El escaneo ya trae BSSID, canal, banda y señal. Dibujar el histograma de canales con
los puntos de acceso del vecino encima y recomendar uno libre **explicando por qué**
(superposición, ancho de 40 MHz, banda) es un diagnóstico con respuesta, no con datos.
**Riesgo:** el escaneo depende del adaptador; en algunos no hay señal de vecinos.

### 2.6 Quién está usando el disco y la red — [m / medio]
CPU por proceso ya existe. Añadir I/O de disco y de red por proceso contesta «quién
tiene el disco al 100 %» sin abrir el Monitor de recursos.
**Dato:** `GetProcessIoCounters` vía P/Invoke, y `Win32_PerfFormattedData_*` por PID.
**Riesgo:** los contadores por proceso son caros; solo bajo demanda.

### 2.7 Decodificador de pantallazos — [c / medio]
`StabilityModule` ya encuentra los minidumps. Leer el código `BUGCHECK` y sus cuatro
parámetros del encabezado del volcado, más el módulo culpables del `MODULES_LIST`,
permite decir «0x116 apuntando a `nvlddmkm.sys`» sin WinDbg. Es parseo, no depuración.

**Hecho en 5.10.** `Core/Diagnostics/BugCheckDecoder.cs` + tabla «Pantallazos».
Con dos correcciones sobre lo previsto: el código de detención **no** está en el
minidump —se lee del informe de errores de Windows (evento 1001 de WER), que es
de donde lo sacan también las herramientas de terceros— y los módulos se listan
como candidatos, no como culpables, porque señalar cuál falló exige análisis de
pila.

### 2.8 Batería: ciclos, voltaje y por qué se apaga al 12 % — [c / medio]
El desgaste ya se reporta. Sumar ciclos de carga, capacidad de diseño contra actual y
voltaje de reposo (`MSFT_Battery`) explica el apagón repentino, que es la queja real.

---

## 3. Herramientas de mantenimiento y reparación

### 3.1 Ensayo antes de aplicar — [c / muy alto]
Un modo «mostrar qué se tocaría, sin tocarlo», que además alimenta el informe para
soporte. `OptimizationBackupStore` ya guarda el estado previo; falta el paso de
presentación. Es la diferencia entre una herramienta que se prueba y una que da miedo.

**Hecho en 5.10.** `TweakModule.Ensayar` y botón «Ensayar (no aplica nada)» en
la ventana de ajustes, primero y a la izquierda del grupo: es la acción sin
consecuencias y hay que encontrarla antes que la que escribe.

### 3.2 Deshacer por paso — [m / muy alto]
Un registro de acciones (fecha, módulo, clave y valor anterior) con un botón por paso.
Convierte la reversión global de «Restaurar estado» en algo granular.

**Hecho en 5.10.** `Core/Windows/ActionLog.cs` + `Ui/CambiosWindow`. No
duplica la lógica de reversión: delega en el módulo que aplicó el cambio, porque
dos reversiones que pueden divergir son peores que una.

### 3.3 Verificación post-cambio en todo — [c / alto]
Releer y comparar después de cada ajuste; si el valor no coincide (política de grupo,
antivirus, `Winlogon` protegido), decir que **no se aplicó**. Hoy esa comprobación
existe solo en parte de los tweaks.

**Hecho en 5.10.** `Core/Windows/ChangeVerifier.cs`, aplicado en
`TweakModule.Aplicar`. `Restore` de optimizaciones y el resto de los módulos
siguen sin verificar: extenderlos es el siguiente paso natural de este lote.

### 3.4 Restauración selectiva — [m / medio]
Si un plan de energía del respaldo ya no existe, hoy falla la restauración completa.
Omitir lo que falta con aviso explícito es mejor que no restaurar nada, y deja de
contradecir el texto del botón.

**Hecho en 5.10.** `OptimizeModule.Restore` omite y nombra lo que ya no
existe, y solo se niega a tocar nada cuando no queda nada restaurable.

### 3.5 Desinstalación asistida de software — [m / medio]
El inventario ya está (`StartupModule`). Una vista que agrupe por editor, tamaño y
fecha, y que abra el desinstalador oficial de cada programa —sin inventar rutas ni
borrar carpetas— cubre una de las tres razones por las que alguien abre una
herramienta así.

---

## 4. Herramientas de datos e informes

### 4.1 Comparar dos diagnósticos — [m / muy alto]
Elegir dos del historial y ver el diff: hallazgos nuevos, desaparecidos, umbrales
cruzados, drivers que envejecieron, disco que perdió salud. Todos los datos ya están
archivados. Es la pantalla de un técnico.

**Hecho en 5.11.** `Core/Diagnostics/ReportDiff.cs` + `Ui/CompararWindow`, con
14 pruebas sobre el motor. Dos decisiones que el análisis original no previó:

- **Los hallazgos se emparejan por área y mensaje, sin la severidad.** Si se
  emparejaran por severidad también, un hallazgo que pasó de Aviso a Crítico
  saldría como «uno resuelto y uno nuevo», que es la lectura contraria a lo que
  pasó. Tampoco cuenta el módulo que lo generó.
- **Solo se comparan mediciones que son número en el modelo.** El desgaste del
  SSD viaja dentro de un texto («12 %») y recuperarlo exige parsear una cadena
  ya formateada: si el formato cambia, la comparación deja de encontrar nada y
  no hay forma de darse cuenta. Queda pendiente hasta el contrato de medición
  de `docs/MEJORAS.md §8.2.1`.

La cobertura se declara antes del puntaje: comparar un «Red» suelto con un
diagnóstico completo da un puntaje que bajó sin que nada empeorara.

### 4.2 Informe «para soporte» con redacción automática — [c / alto]
Nombre de equipo, usuario, rutas y SSID redactados por la app. Hoy el README
**aconseja** editar a mano, y nadie lo hace. Tiene que ser una opción del exportador.

**Hecho en 5.10.** `Core/Diagnostics/Redactor.cs`, sobre una copia. Se
agregaron dos precauciones que el análisis original no previó: sustitución con
límite de palabra (un nombre corto no puede comerse una palabra dentro de
otra) y una cabecera en el propio informe diciendo qué se le quitó.

### 4.3 Exportar a Markdown y PDF — [c / bajo]
Markdown para pegar en un foro o un ticket; PDF por impresión, sin dependencias, para
adjuntar. El HTML ya existe.

**Hecho en 5.10 (Markdown).** `Core/Diagnostics/MarkdownReport.cs`, con
alcance antes de hallazgos: un 92 sobre un diagnóstico incompleto no significa
lo mismo que un 92 sobre uno completo. El PDF sigue pendiente: se haría por
impresión, sin dependencias.

### 4.4 Modo equipos: una tabla de muchos JSON — [c / medio]
`HeadlessRunner` ya corre sin interfaz. Un `--informe-equipos carpeta\*.json` que
produzca una tabla comparativa convierte la app en una herramienta de inventario para
quien atiende varios equipos. Es el salto de «mi PC» a «los PC del laboratorio».

---

## 5. Herramientas que hacen la app más profesional sin medir nada nuevo

### 5.1 Paleta de comandos (Ctrl+K) — [c / muy alto]
Con casi veinte destinos de navegación, módulos, acciones rápidas y ajustes, un campo
que filtra y ejecuta es el atajo más barato para quien ya sabe lo que quiere. Es la
señal menos ambigua de «esto se usa todos los días».

**Hecho en 5.10.** `Ui/PaletteWindow` + botón «Buscar Ctrl+K» en la barra
superior. Un atajo de teclado que nadie ve no existe.

### 5.2 Editor de umbrales — [m / medio]
El puntaje y las reglas tienen números fijos que hoy son un juicio del autor. Un panel
«qué considero alto consumo de disco», con valores por defecto y exportables, convierte
una opinión en una preferencia. Requiere el contrato de medición de
`docs/MEJORAS.md §8.2.1`.

### 5.3 Perfiles editables — [m / medio]
`ProfilesWindow` tiene tres perfiles fijos que no cubren a nadie. Un perfil editable
(lista de tweaks + plan de energía + prioridad), con importar/exportar JSON y respaldo,
es lo que hace que la función exista.

### 5.4 Programador de chequeos — [m / alto]
Diagnóstico diario o semanal, con notificación **solo si algo empeoró**. Requiere el
diff de 4.1 para no convertirse en ruido.

### 5.5 Actualizaciones y firma Authenticode — [m / alto, decisión comercial]
Repartir un .exe sin firmar provoca SmartScreen y enseña al usuario a ignorar las
advertencias de Windows. Una herramienta de diagnóstico no debería entrenar esa
conducta. El costo es el certificado, no el código.

### 5.6 Tema claro y alto contraste — [m / medio]
El tema es un diccionario de tokens, así que es viable. El alto contraste del sistema
(`SystemParameters.HighContrast`) no es opcional en una app que se usa en equipos
ajenos: es la diferencia entre usable y no usable para parte de los usuarios.

---

## 6. Orden sugerido

Cinco lotes, cada uno compilable y verificable por separado.

| # | Lote | Contenido | Por qué aquí |
|---|---|---|---|
| 1 | **Medir en vivo** | 2.1, 2.2, 2.5 | Es lo que se deja abierto y lo que cambia la percepción. |
| 2 | **Confiar** | 3.1, 3.2, 3.3, 3.4, verificación general | Sin ensayo y deshacer, todo lo demás se prueba con miedo. |
| 3 | **Explicar** | 2.3, 2.4, 2.6, 2.7, 4.1, 4.2 | Las herramientas que convierten datos en una causa. |
| 4 | **Producto** | 5.1, 5.2, 5.3, 5.6, 4.3, 4.4 | Lo que hace que se use todos los días y en más de un equipo. |
| 5 | **Distribución** | 5.5, 5.4, i18n | Depende de decisiones comerciales, no solo de código. |

El lote 2 no es negociable en ese orden: agregar herramientas de reparación sin
ensayo ni deshacer aumenta la superficie de daño a la misma velocidad que el valor.

---

## 7. Cómo decidir si una herramienta entra

Cuatro preguntas, en este orden:

1. **¿La respuesta cambia algo que el usuario pueda hacer?** «Tu disco tiene 340
   ciclos restantes» sí. «Tu disco es un modelo X» no.
2. **¿Se puede revertir?** Si no, necesita ensayo, respaldo verificado y confirmación
   explícita. Si aun así no se puede revertir, no entra.
3. **¿Se puede explicar en una frase?** Una medición que necesita tres párrafos de
   contexto es una medición para el autor, no para el usuario.
4. **¿Sigue siendo cierta en un equipo sin administrador?** La mitad de los equipos
   reales no lo son. Si la herramienta solo funciona elevada, tiene que decirlo antes
   de correr y explicar qué se pierde sin elevar.

---

## 8. Verificación

Ninguna de estas herramientas se puede dar por hecha sin las puertas que ya existen.
En Windows, sobre una máquina propia:

```powershell
dotnet restore SysDiag.sln
dotnet build SysDiag.sln -c Release --no-restore
dotnet test  SysDiag.sln -c Release --no-build --logger "trx;LogFileName=tests.trx" --results-directory TestResults
.\Tools\validate_tests.ps1
.\Tools\validate_xaml.ps1 -Root .
.\Tools\validate_fixture.ps1
dotnet publish SysDiag.csproj -c Release -o publish
.\Tools\validate_release.ps1 -Root .\publish
```

Y una regla que este documento quiere dejar escrita: **toda herramienta nueva que
toque el sistema se prueba primero con una cuenta sin administrador**, para ver qué
dice cuando no puede. Ese es el estado en el que la va a encontrar la mitad de los
usuarios.
