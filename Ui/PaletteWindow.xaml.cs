using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SysDiag.Core;

namespace SysDiag.Ui;

/// <summary>Una entrada de la paleta: qué hace, de qué grupo es y cómo ejecutarlo.</summary>
public sealed class Comando
{
    public string Titulo { get; init; } = "";
    public string Grupo { get; init; } = "";
    public string Detalle { get; init; } = "";
    public string Atajo { get; init; } = "";

    /// <summary>Qué ejecutar. El delegado lo provee quien arma la paleta.</summary>
    public Func<Task> Ejecutar { get; init; }

    /// <summary>
    /// Texto por el que se busca. Incluye el grupo y las palabras que alguien
    /// usaría sin saber el nombre exacto del comando: buscar «disco» tiene que
    /// encontrar «Almacenamiento», o la paleta solo sirve para quien ya sabe
    /// qué escribir, que es quien menos la necesita.
    /// </summary>
    public string Sinonimos { get; init; } = "";

    public bool TieneDetalle => !string.IsNullOrWhiteSpace(Detalle);
    public bool TieneAtajo => !string.IsNullOrWhiteSpace(Atajo);
}

/// <summary>
/// Paleta de comandos (Ctrl+K).
///
/// Con casi veinte módulos, una banda de acciones por sección, diecisiete
/// tablas y una docena de botones en el pie, la forma de llegar a algo era
/// acordarse de dónde estaba. Un campo que filtra y ejecuta es el atajo más
/// barato para quien ya sabe lo que quiere, y es también la señal menos
/// ambigua de que una aplicación se usa todos los días.
///
/// No decide nada por su cuenta: cada entrada ejecuta exactamente lo mismo que
/// ejecutaría el botón o el ítem del rail, porque llama al mismo método.
/// </summary>
public partial class PaletteWindow : Window, INotifyPropertyChanged
{
    private readonly List<Comando> _todos;

    public PaletteWindow(IEnumerable<Comando> comandos)
    {
        InitializeComponent();
        // Se ajusta antes de cualquier otra cosa: si la pantalla es más chica
        // que el alto declarado en el XAML, el pie de la ventana quedaría fuera
        // del área de trabajo y no habría cómo arrastrarla de vuelta.
        Ventana.AjustarAPantalla(this);

        // Se materializa una vez: si la lista viniera directamente del caller,
        // cada filtrado volvería a enumerar una secuencia que puede estar
        // construyendo delegados.
        _todos = comandos?.Where(c => c != null && c.Ejecutar != null).ToList() ?? new List<Comando>();
        DataContext = this;
        Filtrar("");

        Loaded += (_, _) =>
        {
            TxtBuscar.Focus();
            Keyboard.Focus(TxtBuscar);
        };
    }

    /// <summary>Se enlaza en el XAML para el estado «no hay coincidencias».</summary>
    public bool SinResultados => Lista.Items.Count == 0;

    private void Filtrar(string consulta)
    {
        string texto = (consulta ?? "").Trim();

        IEnumerable<Comando> visibles = texto.Length == 0
            ? _todos
            : _todos
                .Select(c => (Comando: c, Puntaje: Puntaje(c, texto)))
                .Where(x => x.Puntaje > 0)
                .OrderByDescending(x => x.Puntaje)
                .ThenBy(x => x.Comando.Grupo, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => x.Comando);

        Lista.ItemsSource = visibles.ToList();
        if (Lista.Items.Count > 0) Lista.SelectedIndex = 0;
        Raise(nameof(SinResultados));

        TxtVacio.Text = _todos.Count == 0
            ? "No hay comandos disponibles en este momento."
            : $"Ningún comando coincide con «{texto}». Prueba con «disco», «red», «exportar» o «ajustes».";
    }

    // `SinResultados` se enlaza en el XAML, así que la ventana tiene que
    // avisar cuando cambia: sin esto, el panel de «ninguna coincidencia»
    // aparece y desaparece una pulsación tarde.
    public event PropertyChangedEventHandler PropertyChanged;

    private void Raise([CallerMemberName] string propiedad = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));

    /// <summary>
    /// Puntuación de una coincidencia. Prefiere el principio del título, luego
    /// cualquier palabra del título, luego los sinónimos y el detalle. Es
    /// suficiente para que «red» ponga «Red y latencia» primero y no «Gestionar
    /// la red», que es lo que pasa con un orden alfabético.
    /// </summary>
    private static int Puntaje(Comando c, string texto)
    {
        int puntaje = 0;
        if (c.Titulo.StartsWith(texto, StringComparison.CurrentCultureIgnoreCase)) puntaje += 100;
        else if (Contiene(c.Titulo, texto)) puntaje += 60;

        // Coincidencia de prefijo en cualquier palabra: «latencia» tiene que
        // encontrar «Red y latencia» sin que haya que escribir la primera.
        if (puntaje == 0 && c.Titulo.Split(' ', '·', '-', '(', ')')
                .Any(p => p.Length > 0 && p.StartsWith(texto, StringComparison.CurrentCultureIgnoreCase)))
            puntaje += 40;

        if (Contiene(c.Sinonimos, texto)) puntaje += 20;
        if (Contiene(c.Grupo, texto)) puntaje += 10;
        if (Contiene(c.Detalle, texto)) puntaje += 5;
        return puntaje;
    }

    private static bool Contiene(string donde, string que) =>
        !string.IsNullOrEmpty(donde) && donde.Contains(que, StringComparison.CurrentCultureIgnoreCase);

    // ---- Teclado -----------------------------------------------------------

    private void Buscar_TextChanged(object sender, TextChangedEventArgs e) => Filtrar(TxtBuscar.Text);

    private void Buscar_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                Mover(1); e.Handled = true; break;
            case Key.Up:
                Mover(-1); e.Handled = true; break;
            case Key.Enter:
                _ = EjecutarSeleccion(); e.Handled = true; break;
            case Key.Escape:
                Close(); e.Handled = true; break;
            case Key.Tab:
                // El tabulador no sale de la paleta: no hay nada afuera a lo
                // que valga la pena ir mientras está abierta.
                Mover(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
                e.Handled = true;
                break;
        }
    }

    private void Lista_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: _ = EjecutarSeleccion(); e.Handled = true; break;
            case Key.Escape: Close(); e.Handled = true; break;
        }
    }

    private void Lista_DoubleClick(object sender, MouseButtonEventArgs e) => _ = EjecutarSeleccion();

    private void Mover(int delta)
    {
        if (Lista.Items.Count == 0) return;
        int indice = Lista.SelectedIndex + delta;
        if (indice < 0) indice = Lista.Items.Count - 1;
        if (indice >= Lista.Items.Count) indice = 0;
        Lista.SelectedIndex = indice;
        Lista.ScrollIntoView(Lista.SelectedItem);
    }

    private async Task EjecutarSeleccion()
    {
        if (Lista.SelectedItem is not Comando comando) return;

        // Se cierra antes de ejecutar: el comando puede abrir su propio
        // diálogo modal, y una paleta encima de un diálogo modal deja la
        // aplicación en un estado raro si el usuario vuelve atrás.
        Close();
        try
        {
            await comando.Ejecutar();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Comando «{comando.Titulo}»: {ex.Message}", "ERROR");
            Dialog.Error("No se pudo ejecutar el comando", ex.Message);
        }
    }
}
