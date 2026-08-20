namespace Soratus.Portal.Components.Shared;

/// <summary>
/// Eén blok van een sparkline: wat er in twee uur is gedraaid.
/// </summary>
/// <param name="Runs">Het aantal runs in dit blok van twee uur. Nul betekent een leeg blok.</param>
/// <param name="Failed">Hoeveel van die runs zijn mislukt. Eén is genoeg om het blok rood te maken.</param>
/// <remarks>
/// Eén mislukking maakt het hele blok rood, ook naast negen geslaagde runs. Dat is bewust: de
/// sparkline is een zoeklicht op storingen, geen verhouding.
/// </remarks>
public readonly record struct SparkBlock(int Runs, int Failed);
