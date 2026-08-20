namespace Soratus.Portal.Components.Shared;

/// <summary>
/// Of een veld te bewerken is, of als feit wordt getoond.
/// </summary>
/// <remarks>
/// §8 is hier expliciet over: <b>read-only is platte tekst, geen uitgegrijsd veld</b>. Een
/// uitgegrijsd veld zegt "je mag dit niet"; platte tekst zegt "dit is een feit". Voor een klant
/// is het contract een feit — hij ziet dus geen randen, geen vulling en geen cursor die belooft
/// dat er iets te typen valt.
///
/// De stand komt daarom niet per veld uit de markup maar uit een <see cref="FieldScope"/> die de
/// <c>FormCard</c> naar beneden cascadeert. Een scherm zet één stand op de kaart; de velden
/// volgen. Zou elk veld zelf kiezen, dan is één vergeten veld genoeg om het patroon te breken,
/// en dat is precies wat er in dit werk al drie keer met gekopieerde CSS is gebeurd.
///
/// <see cref="ReadOnly"/> is bewust de eerste waarde en dus de standaard van een
/// <c>default(FieldMode)</c>: een veld dat buiten een formulier terechtkomt hoort tekst te zijn,
/// niet een invoervak zonder plek om naartoe te schrijven. De zachtste fout is de standaard.
/// </remarks>
public enum FieldMode
{
    /// <summary>Platte tekst. Label plus waarde, geen invoervak.</summary>
    ReadOnly,

    /// <summary>Een invoervak: 1px <c>--line-field</c>, radius 6px, focusrand <c>--blue</c>.</summary>
    Edit,
}
