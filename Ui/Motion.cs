using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SysDiag.Ui;

/// <summary>
/// Capa de movimiento de la interfaz.
///
/// Tres reglas gobiernan todo lo que se anima aquí:
///
///  · Se mueve solo lo que cambió. Una cifra que aparece de golpe no dice
///    nada; una que sube de 0 a 82 dice que algo se midió. Ninguna animación
///    existe por decoración: cada una marca una transición de estado real
///    (empezar a medir, terminar, cambiar de vista, cambiar un valor).
///
///  · Nada de layout por cuadro. Solo se animan Opacity y transformaciones de
///    render: WPF los compone sin volver a medir. Animar Margin, Width o el
///    Color de un pincel compartido reabriría el layout de la vista o pintaría
///    de otro color a todos los que usan ese pincel del tema. Por eso los
///    hovers del tema usan una capa superpuesta que se desvanece, y no un
///    cambio de Background. La única excepción es el número del puntaje, cuya
///    caja está centrada en un hueco fijo: medirlo no corre a sus vecinos.
///
///  · Se puede apagar. Si el sistema pidió no animar los controles se aplica
///    el valor final en seco: mismo resultado, sin movimiento. La información
///    nunca depende de la animación; la animación solo la hace legible.
///
/// Las curvas y las duraciones de referencia viven en Ui/Theme.xaml
/// (EAnimRapida, EAnimBase, EAnimLenta). Acá se repiten los números a
/// propósito: el código no puede leer el diccionario de recursos antes de que
/// la aplicación exista, y duplicar tres constantes cuesta menos que abrir una
/// dependencia del motor de movimiento hacia el árbol visual.
/// </summary>
public static class Motion
{
    /// <summary>Cambio de estado del puntero: tiene que sentirse inmediato.</summary>
    public static readonly TimeSpan Rapida = TimeSpan.FromMilliseconds(110);

    /// <summary>Lo que entra y sale de pantalla.</summary>
    public static readonly TimeSpan Base = TimeSpan.FromMilliseconds(200);

    /// <summary>Lo que acompaña un resultado nuevo (puntaje, atenuar la vista).</summary>
    public static readonly TimeSpan Lenta = TimeSpan.FromMilliseconds(340);

    /// <summary>Desplazamiento de entrada, en píxeles. Corto: es un empujón, no un viaje.</summary>
    private const double Pasada = 12;

    /// <summary>
    /// Paso de la cascada y tope. Con un tope, cuarenta tarjetas siguen entrando
    /// en menos de un segundo; sin él, la última empezaría a los tres segundos.
    /// </summary>
    private const double Cascada = 24;
    private const int CascadaMaxima = 12;

    /// <summary>
    /// Preferencia del sistema para mostrar animaciones en las ventanas. Cuando
    /// está apagada se aplican los valores finales sin transición.
    /// </summary>
    public static bool Animar => SystemParameters.ClientAreaAnimation;

    private static IEasingFunction Suave() => new CubicEase { EasingMode = EasingMode.EaseOut };

    // ======================================================================
    //  Entrada: la vista aparece desvaneciéndose y subiendo un poco
    // ======================================================================

    public static readonly DependencyProperty EnterProperty = DependencyProperty.RegisterAttached(
        "Enter", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnEnterChanged));

    public static bool GetEnter(DependencyObject o) => (bool)o.GetValue(EnterProperty);
    public static void SetEnter(DependencyObject o, bool value) => o.SetValue(EnterProperty, value);

    /// <summary>Retardo de la entrada, para escalonar regiones sin coordinarlas a mano.</summary>
    public static readonly DependencyProperty EnterDelayProperty = DependencyProperty.RegisterAttached(
        "EnterDelay", typeof(TimeSpan), typeof(Motion), new PropertyMetadata(TimeSpan.Zero));

    public static TimeSpan GetEnterDelay(DependencyObject o) => (TimeSpan)o.GetValue(EnterDelayProperty);
    public static void SetEnterDelay(DependencyObject o, TimeSpan value) => o.SetValue(EnterDelayProperty, value);

    private static void OnEnterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || d is not FrameworkElement el) return;

        // Se anima cada vez que el elemento pasa a visible, no solo al cargar:
        // las cuatro vistas comparten el mismo hueco y se muestran por
        // Visibility, así que «volver a ser visible» ES el cambio de estado.
        el.IsVisibleChanged += (_, _) =>
        {
            if (!el.IsVisible || !Animar) return;

            var paso = new TranslateTransform(0, 0);   // valor base: sin desplazamiento
            el.RenderTransform = paso;

            // CacheMode durante la transición, y nada después: una vista entera
            // es un árbol grande y sus tarjetas llevan sombra difusa. Animar la
            // opacidad del contenedor sin caché obliga a recomponer (y re-sombrear)
            // cada cuadro; con el bitmap intermedio el coste por cuadro deja de
            // depender de lo complicada que sea la vista. Se quita al terminar
            // porque el caché cambia el suavizado del texto: solo vale mientras
            // el contenido se está moviendo.
            var cache = new BitmapCache { EnableClearType = false };
            el.CacheMode = cache;

            var opacidad = Transicion(el, UIElement.OpacityProperty, 0, 1, Lenta, TimeSpan.Zero);
            opacidad.Completed += (_, _) => el.CacheMode = null;

            Transicion(paso, TranslateTransform.YProperty, Pasada, 0, Lenta, TimeSpan.Zero);
        };
    }

    // ======================================================================
    //  Atenuar: mientras se mide, lo que sigue en pantalla es de la corrida
    //  anterior. El ViewModel ya lo indicaba; lo que faltaba es que el cambio
    //  no diera un salto.
    // ======================================================================

    /// <summary>
    /// Opacidad a la que tiene que estar el elemento. Cada cambio se interpola,
    /// así que el valor sigue pudiendo venir de un binding del estado
    /// (<code>ui:Motion.Fade="{Binding ContenidoOpacidad}"</code>) en lugar de
    /// tener que coordinarse desde el code-behind.
    /// </summary>
    public static readonly DependencyProperty FadeProperty = DependencyProperty.RegisterAttached(
        "Fade", typeof(double), typeof(Motion), new PropertyMetadata(1.0, OnFadeChanged));

    public static double GetFade(DependencyObject o) => (double)o.GetValue(FadeProperty);
    public static void SetFade(DependencyObject o, double value) => o.SetValue(FadeProperty, value);

    private static void OnFadeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        double destino = Doble(e.NewValue);
        if (double.IsNaN(destino)) return;
        destino = Math.Clamp(destino, 0, 1);

        if (!Animar)
        {
            el.SetValue(UIElement.OpacityProperty, destino);
            return;
        }

        // Desde el valor efectivo: si se interrumpe una atenuación a medias, la
        // vuelta arranca donde está la opacidad real y no desde 1.
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(el.Opacity, destino, Lenta)
        {
            EasingFunction = Suave()
        });
    }

    // ======================================================================
    //  Contador y arco: el puntaje se cuenta y se traza, no se reemplaza
    // ======================================================================

    /// <summary>
    /// Valor al que tiene que llegar el número. Se enlaza al dato
    /// (ui:Motion.Number="{Binding Puntaje}") y el TextBlock deja de enlazar
    /// su propio Text: lo escribe este animador.
    /// </summary>
    public static readonly DependencyProperty NumberProperty = DependencyProperty.RegisterAttached(
        "Number", typeof(double), typeof(Motion), new PropertyMetadata(double.NaN, OnNumberChanged));

    public static double GetNumber(DependencyObject o) => (double)o.GetValue(NumberProperty);
    public static void SetNumber(DependencyObject o, double value) => o.SetValue(NumberProperty, value);

    private static readonly DependencyPropertyKey AnimatedNumberPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly("AnimatedNumber", typeof(double), typeof(Motion),
            new PropertyMetadata(0.0, OnAnimatedNumberChanged));

    public static readonly DependencyProperty AnimatedNumberProperty = AnimatedNumberPropertyKey.DependencyProperty;

    /// <summary>
    /// Igual que NumberProperty pero para el anillo: el valor 0-100 se interpola
    /// y la geometría se recalcula en cada cuadro, así el arco barre hasta el
    /// puntaje nuevo en lugar de aparecer ya dibujado.
    /// </summary>
    public static readonly DependencyProperty SweepProperty = DependencyProperty.RegisterAttached(
        "Sweep", typeof(double), typeof(Motion), new PropertyMetadata(double.NaN, OnSweepChanged));

    public static double GetSweep(DependencyObject o) => (double)o.GetValue(SweepProperty);
    public static void SetSweep(DependencyObject o, double value) => o.SetValue(SweepProperty, value);

    private static readonly DependencyPropertyKey AnimatedSweepPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly("AnimatedSweep", typeof(double), typeof(Motion),
            new PropertyMetadata(0.0, OnAnimatedSweepChanged));

    public static readonly DependencyProperty AnimatedSweepProperty = AnimatedSweepPropertyKey.DependencyProperty;

    private static void OnNumberChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        double destino = Doble(e.NewValue);
        if (double.IsNaN(destino)) return;

        double actual = Doble(tb.GetValue(AnimatedNumberProperty));
        if (!SePuedeAnimar(tb, actual, destino))
        {
            Fijar(tb, AnimatedNumberPropertyKey, destino);
            tb.Text = Formato(destino);
            return;
        }

        Interpolar(tb, AnimatedNumberProperty, AnimatedNumberPropertyKey, actual, destino, Base,
            alTerminar: final => tb.Text = Formato(final));
    }

    /// <summary>
    /// Cada cuadro de la interpolación pasa por aquí: es lo que escribe el
    /// número intermedio en la etiqueta.
    /// </summary>
    private static void OnAnimatedNumberChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock tb) tb.Text = Formato(Doble(e.NewValue));
    }

    private static void OnSweepChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Path path) return;
        double destino = Doble(e.NewValue);
        if (double.IsNaN(destino)) return;

        double actual = Doble(path.GetValue(AnimatedSweepProperty));
        if (!SePuedeAnimar(path, actual, destino))
        {
            Fijar(path, AnimatedSweepPropertyKey, destino);
            return;
        }

        // El arco es el titular de la pantalla: merece más tiempo que una fila.
        Interpolar(path, AnimatedSweepProperty, AnimatedSweepPropertyKey, actual, destino, Lenta, null);
    }

    private static void OnAnimatedSweepChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Path path) path.Data = ScoreArc.Geometria(Doble(e.NewValue));
    }

    private static bool SePuedeAnimar(FrameworkElement el, double actual, double destino) =>
        Animar && el.IsLoaded && !double.IsNaN(actual) && Math.Abs(actual - destino) > 0.001;

    /// <summary>
    /// Interpola un adjunto <b>distinto</b> del que se enlazó. Es el detalle que
    /// hace que esto sea correcto: si se animara la misma propiedad que trae el
    /// binding, el valor animado taparía el valor nuevo de la fuente y el aviso
    /// de cambio no volvería a dispararse nunca. Con dos propiedades —una
    /// enlazada, una animada— re-apuntar a mitad de camino funciona solo.
    /// </summary>
    private static void Interpolar(DependencyObject objetivo, DependencyProperty animada,
        DependencyPropertyKey baseKey, double desde, double hasta, TimeSpan duracion,
        Action<double> alTerminar)
    {
        var el = (UIElement)objetivo;
        var anim = new DoubleAnimation(desde, hasta, duracion)
        {
            EasingFunction = Suave(),
            FillBehavior = FillBehavior.HoldEnd
        };

        anim.Completed += (_, _) =>
        {
            // Se fija el valor base antes de soltar la animación: entre las dos
            // líneas no hay un cuadro intermedio, así que no hay parpadeo.
            objetivo.SetValue(baseKey, hasta);
            el.BeginAnimation(animada, null);
            alTerminar?.Invoke(hasta);
        };

        el.BeginAnimation(animada, anim);
    }

    private static void Fijar(DependencyObject target, DependencyPropertyKey key, double valor)
    {
        if (Doble(target.GetValue(key.DependencyProperty)) == valor) return;
        target.SetValue(key, valor);
    }

    private static double Doble(object value) => value switch
    {
        double d => d,
        int i => i,
        _ => double.NaN
    };

    private static string Formato(double valor) =>
        Math.Round(valor, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Arranca una animación simple con retardo. Sin Storyboard: un solo
    /// animador por propiedad y por elemento basta, y así no hay que resolver
    /// nombres ni ámbitos para coordinar dos tramos.
    ///
    /// El destino es <see cref="IAnimatable"/> y no <c>Animatable</c>: la
    /// opacidad se anima sobre un <c>UIElement</c> (un Grid, un Border), que
    /// implementa la interfaz sin derivar de esa clase, y el desplazamiento
    /// sobre un <c>TranslateTransform</c>, que sí deriva. Con <c>Animatable</c>
    /// la mitad de las llamadas no compila.
    /// </summary>
    private static DoubleAnimation Transicion(IAnimatable target, DependencyProperty propiedad,
        double desde, double hasta, TimeSpan duracion, TimeSpan retardo)
    {
        var anim = new DoubleAnimation(desde, hasta, duracion)
        {
            EasingFunction = Suave(),
            BeginTime = retardo,
            FillBehavior = FillBehavior.Stop
        };
        target.BeginAnimation(propiedad, anim);
        return anim;
    }

    // ======================================================================
    //  Cascada de contenedores
    // ======================================================================

    /// <summary>
    /// Entra cada fila unos milisegundos después de la anterior. Se usa en las
    /// listas de datos —tarjetas de medición y barras del gráfico— porque el ojo
    /// puede seguir un recorrido y no tiene que descubrir de golpe veinte
    /// números nuevos.
    /// </summary>
    public static readonly DependencyProperty StaggerProperty = DependencyProperty.RegisterAttached(
        "Stagger", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnStaggerChanged));

    public static bool GetStagger(DependencyObject o) => (bool)o.GetValue(StaggerProperty);
    public static void SetStagger(DependencyObject o, bool value) => o.SetValue(StaggerProperty, value);

    private static void OnStaggerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || d is not ItemsControl ic) return;

        ic.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            if (ic.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated) return;
            if (!Animar) return;

            int n = ic.Items.Count;
            for (int i = 0; i < n; i++)
            {
                // Un contenedor que la virtualización todavía no materializó
                // devuelve null: entra sin animación, no hay nada que animar
                // fuera de pantalla.
                if (ic.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement cont) continue;

                var paso = cont.RenderTransform as TranslateTransform;
                if (paso == null)
                {
                    paso = new TranslateTransform(0, 0);
                    cont.RenderTransform = paso;
                }

                TimeSpan retardo = TimeSpan.FromMilliseconds(Math.Min(i, CascadaMaxima) * Cascada);
                Transicion(cont, UIElement.OpacityProperty, 0, 1, Base, retardo);
                Transicion(paso, TranslateTransform.YProperty, 8, 0, Base, retardo);
            }
        };
    }

    // ======================================================================
    //  Elevación al pasar el puntero
    // ======================================================================

    /// <summary>
    /// Levanta apenas la superficie al pasar el puntero por encima. Se anima un
    /// ScaleTransform creado por elemento —y no el Background— porque un Border
    /// sin plantilla no tiene cómo interpolar un color sin tocar el pincel
    /// compartido del tema, que repaintaría también a los demás.
    /// </summary>
    public static readonly DependencyProperty LiftProperty = DependencyProperty.RegisterAttached(
        "Lift", typeof(double), typeof(Motion), new PropertyMetadata(0.0, OnLiftChanged));

    public static double GetLift(DependencyObject o) => (double)o.GetValue(LiftProperty);
    public static void SetLift(DependencyObject o, double value) => o.SetValue(LiftProperty, value);

    private static void OnLiftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el) return;
        double amount = (double)e.NewValue;
        if (amount <= 0) return;

        // Una transformación por elemento, asignada desde el código. Un Setter
        // de Style compartiría (y sellaría) la misma instancia entre todas las
        // tarjetas, y una Freezable sellada no se puede animar.
        el.RenderTransform = new ScaleTransform(1, 1);
        el.RenderTransformOrigin = new Point(0.5, 0.5);

        el.MouseEnter += (_, _) => Elevar(el, 1 + amount);
        el.MouseLeave += (_, _) => Elevar(el, 1);
    }

    private static void Elevar(FrameworkElement el, double destino)
    {
        if (el.RenderTransform is not ScaleTransform escala) return;

        if (!Animar)
        {
            escala.ScaleX = escala.ScaleY = destino;
            return;
        }

        foreach (var propiedad in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            escala.BeginAnimation(propiedad, new DoubleAnimation(destino, Rapida) { EasingFunction = Suave() });
    }
}
