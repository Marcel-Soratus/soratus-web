namespace Soratus.Portal.Components.Shared;

/// <summary>
/// Eén kolom van een datatabel: zijn kop, zijn grid-track en hoe de cel zich gedraagt.
/// </summary>
/// <param name="Header">
/// De kolomkop. Verschijnt in <c>DataRowHeader</c> (mono, uppercase) en onder 768px als label
/// boven de celwaarde. Ook wat een schermlezer vóór de celwaarde hoort.
/// </param>
/// <param name="Track">
/// De grid-track, letterlijk zoals CSS hem leest: <c>minmax(0, 2fr)</c>, <c>84px</c>,
/// <c>max-content</c>. Gebruik bij flexibele kolommen <c>minmax(0, …)</c> zodat een lange
/// ononderbroken tekenreeks de tabel niet oprekt.
/// </param>
/// <param name="Visible">
/// Of de kolom meedoet. <c>false</c> laat hem uit de track-lijst én uit de kop verdwijnen, zodat
/// een scherm een kolom per rol kan verbergen zonder een tweede <see cref="RowGrid"/> te bouwen.
/// Render dan ook geen <c>DataCell</c> voor die kolom.
/// </param>
/// <param name="Align">De uitlijning van kop en cel.</param>
/// <param name="Labelled">
/// Of de cel een label krijgt: het verborgen voorvoegsel voor schermlezers en het zichtbare
/// label in de tweekoloms weergave onder 768px. Zet dit op <c>false</c> voor de naamkolom en
/// voor kolommen zonder kop — "Klant Bakker Logistiek" voegt niets toe.
/// </param>
/// <remarks>
/// De kolomdefinitie staat bewust in C# en niet in CSS. Er komen acht tabellen in dit portaal;
/// als elk scherm zijn eigen <c>grid-template-columns</c> in eigen CSS zet, moet elk scherm ook
/// zijn eigen responsieve regel schrijven. Nu leest <c>DataCard</c> deze definitie en volgt de
/// responsieve regel uit <c>layout.css</c> automatisch.
/// </remarks>
public readonly record struct GridColumn(
    string Header,
    string Track,
    bool Visible = true,
    CellAlign Align = CellAlign.Start,
    bool Labelled = true);
