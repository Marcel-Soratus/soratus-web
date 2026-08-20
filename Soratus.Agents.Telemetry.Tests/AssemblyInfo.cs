using Xunit;

// Deze tests bouwen elk een echte host met achtergronddiensten, kanalen en een afsluitende
// leegdraaislag. xUnit draait testklassen standaard parallel, dus dan lopen er meerdere van die
// hosts tegelijk. Eén keer leverde dat een test op die in isolatie en in drie volledige runs daarna
// slaagde — en een test die soms rood is, is erger dan een test die rood is: hij leert je rood
// negeren. Dat is precies de fout die deze hele reeks maakte, in een andere vorm.
//
// In productie draait er één host per proces, dus dit is een eigenschap van de testopstelling en
// geen defect in de bibliotheek: binnen één host is het kanaal expliciet meervoudig-schrijvend en
// enkelvoudig-lezend, en de leegdraaislag is er één. Serialiseren kost hier niets — de hele suite
// loopt in een halve seconde — en levert een meting op die elke keer hetzelfde zegt.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
