# Sistema de diseño de SysDiag

Este documento describe cómo se ve SysDiag y por qué. No es una galería: es el
conjunto de reglas con el que se construyó la interfaz, para que la próxima
ventana o la próxima tarjeta se dibujen igual que las anteriores sin tener que
adivinar.

Todo lo que aparece aquí vive en `Ui/Theme.xaml`, que es la única fuente de
tokens. **Un color, un radio o un tamaño de letra escritos a mano en una vista
son una deuda**: mañana no se pueden cambiar en un solo lugar.

---

## 1. Paleta

### Superficies

La jerarquía la da la elevación, no el borde: cuanto más arriba está un
elemento, más clara es su superficie. El borde de 1 px solo define el canto.

| Token | Valor | Uso |
|---|---|---|
| `CBase` | `#0B1020` | Fondo del contenido |
| `CRail` | `#080C18` | Barra superior y rail de navegación (más profundo que el contenido) |
| `CSurface` | `#141A2E` | Tarjeta |
| `CSurfaceAlt` | `#1B2340` | Tarjeta elevada, hover de fila, campo de texto |
| `CSurfaceHi` | `#242E52` | Superficie flotante por encima de todo (información emergente) |
| `CStrokeSoft` | `#1A2240` | Borde discreto: separa sin dividir |
| `CStroke` | `#2C3763` | Borde visible: cierra un bloque o un campo |

### Texto

| Token | Valor | Uso |
|---|---|---|
| `CText` | `#EDEFFA` | Texto principal |
| `CTextDim` | `#A6B0D4` | Texto secundario y notas |
| `CTextMuted` | `#6E79A8` | Rótulos, etiquetas y texto de tercer nivel |

### Acentos

| Token | Valor | Uso |
|---|---|---|
| `CAccent` | `#8B7CFF` | Violeta de marca: acción principal, selección, gráfico de evolución |
| `CAccent2` | `#38D6F0` | Cian del logo: **solo lo que está vivo ahora** (operación en curso, traza del monitor de ping, puntos del historial) |
| `COk` | `#35D6A0` | Dato bueno |
| `CWarn` | `#F5A524` | Dato que merece atención |
| `CBad` | `#FF5470` | Dato crítico |

### Reglas de uso del color

1. **Un solo acento por pantalla.** El violeta señala la acción; el cian, el
   momento. Si ambos aparecen con el mismo peso, ninguno orienta.
2. **El color semántico califica datos, nunca decora.** Verde, ámbar y rojo se
   usan para el valor de una medición, la severidad de un hallazgo o una línea
   del registro. No se usan para «alegrar» una tarjeta.
3. **Degradados solo donde hay jerarquía que mostrar.** `GAccent` (botón
   principal), `GHero` (tarjeta del puntaje), `GRail` (barra lateral) y `GPulse`
   (progreso indeterminado). En cualquier otro sitio, el color va plano.
4. **El texto sobre violeta es `BInk`** (`#0A0D1A`), nunca blanco: el contraste
   es mayor y el botón se lee como un objeto sólido.

### Superposiciones

En vez de aclarar u oscurecer el fondo de cada control, los estados se pintan
con capas translúcidas que funcionan igual sobre cualquier superficie:
`BHover` (6 % blanco), `BAccentWash` (16 % violeta), `BAccent2Wash` (14 % cian),
`BTrack` (7 % blanco, para rieles de gráfico).

---

## 2. Tipografía

Tres roles, ni uno más.

| Rol | Familia | Tamaños | Uso |
|---|---|---|---|
| Display | Segoe UI Variable Display | 30 (métrica), 24 (H1), 17 (H2) | Cifras grandes y titulares |
| Texto | Segoe UI Variable Text | 13 (cuerpo), 12 (nota), 10.5 (rótulo) | Todo lo que se lee seguido |
| Mono | Cascadia Mono | 12 | **Todo dato numérico y toda tabla** |

El peso lo pone el estilo (`SemiBold`), no el nombre de la familia: así la
interfaz se ve igual en Windows 10, donde Segoe UI Variable no existe y la
familia cae a Segoe UI.

Las cifras van siempre en monoespaciada porque tienen ancho fijo: una columna
que se actualiza en vivo no puede «bailar» al cambiar de dígitos.

---

## 3. Escalas

- **Espaciado**: múltiplos de 4, con 8 como unidad de ritmo. Márgenes exteriores
  de 24, interiores de 16, separaciones cortas de 8 o 12.
- **Radios**: `RSm` 5 (botones, campos, chips), `RMd` 10 (tarjetas), `RLg` 14
  (paneles flotantes), `RPill` 12 (cápsulas).
- **Sombras**: `CardShadow` (14 px de blur) para lo que apoya sobre el fondo;
  `PanelShadow` (30 px) para lo que flota sobre la aplicación.

---

## 4. Estructura de la ventana principal

```
┌───────────────────────────────────────────────────────────────┐
│ BARRA SUPERIOR (52)   logo · equipo          estado · reloj · ▁ ▢ ✕ │
├──────────┬────────────────────────────────────────────────────┤
│ RAIL     │ CABECERA DEL MÓDULO     título + [Resumen|…]       │
│ (182)    ├────────────────────────────────────────────────────┤
│          │                                                    │
│ [Botón   │ VISTA ACTIVA (Resumen / Hallazgos / Datos /        │
│  princi- │ Registro)                                          │
│  pal]    │                                                    │
│          │                                                    │
│ Análisis ├────────────────────────────────────────────────────┤
│ Manten.  │ PIE: progreso + acciones                           │
│ Sistema  │                                                    │
│ Datos    │                                                    │
└──────────┴────────────────────────────────────────────────────┘
```

**Por qué esta disposición**

- **Barra superior**: la marca y el estado del equipo valen en cualquier
  módulo, así que no pueden vivir dentro de uno. El reloj y el indicador de
  corrida se ven siempre, incluso con la vista desplazada.
- **Rail en lugar de panel**: con 16 destinos, un panel de 252 px le quitaba al
  contenido un 19 % del ancho para mostrar una lista que casi siempre se usa de
  una en una. El rail mantiene las etiquetas completas (no iconos solos: «Punto
  de restauración» dicho con un icono es un acertijo) y devuelve 70 px a las
  tablas.
- **La acción principal separada**: el diagnóstico completo es lo que se hace al
  abrir la aplicación. Como ítem de lista competía con «Ajustes» por el mismo
  peso visual; como botón, la jerarquía queda dicha.
- **Grupos con rótulo**: «Análisis», «Mantenimiento», «Sistema» y «Datos»
  describen la intención, no el orden de implementación. Lo que mide, lo que
  cambia el equipo, lo que protege y lo que guarda.

**Orden del resumen**: puntaje → siguiente paso → mediciones → gráficos. El
puntaje dice cuánto; «siguiente paso» dice qué hacer con eso; las mediciones y
los gráficos sostienen ambos. Antes «siguiente paso» cerraba la página y se
leía como una nota al pie.

**Hallazgos en dos paneles**: la recomendación del hallazgo seleccionado va
fija a la derecha. Apilada bajo la lista, con más de cuatro hallazgos quedaba
fuera de la pantalla justo cuando más se la necesita.

---

## 5. Componentes

| Estilo | Es | Notas |
|---|---|---|
| `BtnPrimary` | Acción principal | Degradado violeta, texto en `BInk`. Uno por vista. |
| `BtnGhost` | Acción secundaria con caja | Para confirmaciones y diálogos. |
| `BtnQuiet` | Acción secundaria sin caja | Para barras con varias acciones. Sin borde ni en hover. |
| `BtnCaption` / `BtnCaptionClose` | Controles de ventana | El cierre se pinta en rojo al pasar el cursor. |
| `SegmentBar` + `Segment` | Conmutador de vistas | El activo se rellena de violeta. |
| `Card` / `CardOutline` / `HeroCard` | Superficies | `HeroCard` es la única con degradado: reservada al puntaje. |
| `MetricCard` | Tarjeta de medición | Rótulo, cifra, regla de escala, nota y módulo de origen. |
| `Chip` / `MiniChip` | Distintivos | Severidad y procedencia del dato. |
| `PanelShell` | Armazón de ventanas flotantes | Lo usan los cinco diálogos y paneles de opciones. |
| `Opcion` | Casilla con franja de riesgo | El color de la franja lo pone quien la declara. |
| `BarTemplate` | Barra de gráfico | Con riel de fondo que hace visible la escala. |
| `CampoNumero` / `FilaAjuste` | Campo y fila de ajustes | Definidos una vez, usados por Ajustes. |

### Regla de escala

Cada métrica lleva debajo una serie de marcas que se llenan según su magnitud,
como la escala de un instrumento analógico. Es el elemento que da identidad a la
interfaz y codifica lo que la aplicación hace: medir. El ancho de las marcas se
calcula en `MetricCard.Create` (`FillStar` / `RestStar`); la vista solo las
dibuja.

---

## 6. Añadir algo nuevo

1. **¿Existe ya un token para eso?** Úsalo. Si el color que necesitas no está en
   la paleta, casi siempre significa que estás a punto de romper una de las
   reglas de la sección 1.
2. **¿Es una ventana flotante?** Empieza por `PanelShell` y añade la barra de
   título con la marca, como en Monitor de ping e Historial.
3. **¿Es una superficie nueva?** `Card`, no un `Border` con fondo escribo a mano.
4. **¿Muestra un número?** `FMono`, y preferiblemente la regla de escala si es
   una magnitud comparable.
5. **Cualquier vista nueva se construye en el autotest del CI** (`App.xaml.cs`,
   `--self-test`). Si tu XAML tiene un error, el CI lo ve antes que el usuario:
   añade la ventana ahí.

## 7. Lo que no se hace

- Escribir un color en hexadecimal dentro de una vista.
- Usar `MessageBox` (se dibuja en claro y rompe el conjunto): usar `Dialog`.
- Usar la barra de título del sistema en una ventana nueva: no se puede
  tematizar.
- Usar verde, ámbar o rojo para algo que no sea un dato o una severidad.
- Añadir un cuarto rol tipográfico.
