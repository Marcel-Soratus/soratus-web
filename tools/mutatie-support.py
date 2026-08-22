"""De mutatielijst van de supportkant (§46 van de fase-0-afwijkingen).

Alleen de lijst. Het schrijven, terugzetten, bouwen en meten staat in `tools/mutatie.py`; lees de
waarschuwing bovenaan dat bestand voordat je dit script start, want het schrijft in productiebestanden.

Draait alleen de tests in `Soratus.Portal.Tests.Support`. Dat is bewust: de vraag per mutatie is welke
van de tests van deze lane rood wordt. Wat er in de rest van de suite gebeurt is de eindmeting, en die
staat apart.

M1 t/m M22 horen rood te maken. S1 t/m S6 zijn met opzet stil: er hoort geen test op te staan, en ze
staan hier zodat ze bij een volgende ronde niet opnieuw als vondst worden gemeld. Zie §46.10.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import mutatie  # noqa: E402


MUTATIES = [
    # M5 raakt drie plekken in hetzelfde bestand en moet dus in één keer worden toegepast. In stukken
    # compileert de tussenstand niet, en een eerdere versie van de meetlaag las dat als groen -- de
    # tweede meetval uit tools/mutatie.py. Vandaar `samengesteld` en niet drie losse mutaties.
    #
    # Hij staat vooraan omdat hij een andere vorm heeft en niet omdat de volgorde iets betekent: de
    # nummering is de leesorde uit het rapport en niet de draaiorde.
    mutatie.samengesteld("M5  de vraag wordt pas na de eerstelijn vastgelegd", [
        (
            "Soratus.Portal/Support/SupportDesk.cs",
            """        var posted = await store.PostQuestionAsync(scope, question, cancellationToken)
            .ConfigureAwait(false);

        if (!posted.IsSaved)
        {
            return posted;
        }

        if (services.GetService<ISupportFirstLine>() is not { } firstLine)""",
            """        if (services.GetService<ISupportFirstLine>() is not { } firstLine)""",
        ),
        (
            "Soratus.Portal/Support/SupportDesk.cs",
            """                scope.CustomerId);

            return posted;
        }""",
            """                scope.CustomerId);

            return await store.PostQuestionAsync(scope, question, cancellationToken)
                .ConfigureAwait(false);
        }""",
        ),
        (
            "Soratus.Portal/Support/SupportDesk.cs",
            """        var recorded = await store
            .RecordFirstLineAsync(scope, enquiry, answer, cancellationToken)
            .ConfigureAwait(false);""",
            """        var posted = await store.PostQuestionAsync(scope, question, cancellationToken)
            .ConfigureAwait(false);

        if (!posted.IsSaved)
        {
            return posted;
        }

        var recorded = await store
            .RecordFirstLineAsync(scope, enquiry, answer, cancellationToken)
            .ConfigureAwait(false);""",
        ),
    ]),
    mutatie.enkel(
        'M1  Accept neemt elke grondslag aan (subsetcontrole eruit)',
        'Soratus.Portal/Support/CosmosSupportStore.cs',
        'return enquiry.Grounds.Contains(ground) ? ground : null;',
        'return ground;',
    ),
    mutatie.enkel(
        'M2  Accept toetst alleen de soort en niet de aanduiding',
        'Soratus.Portal/Support/CosmosSupportStore.cs',
        'return enquiry.Grounds.Contains(ground) ? ground : null;',
        'return enquiry.Grounds.Any(g => g.Kind == ground.Kind) ? ground : null;',
    ),
    mutatie.enkel(
        'M3  Accept vergelijkt op instantie in plaats van op waarde',
        'Soratus.Portal/Support/CosmosSupportStore.cs',
        'return enquiry.Grounds.Contains(ground) ? ground : null;',
        'return enquiry.Grounds.Any(g => ReferenceEquals(g, ground)) ? ground : null;',
    ),
    mutatie.enkel(
        'M4  SupportText.Answer verzint een zin in plaats van het feit te nemen',
        'Soratus.Portal/Support/SupportText.cs',
        '        return ground.Fact;',
        '        return "Over juli 2026 staat \u20ac 0,00 door te belasten.";',
    ),
    mutatie.enkel(
        'M6  de uitzondering van de naad wordt niet opgevangen',
        'Soratus.Portal/Support/SupportDesk.cs',
        '        catch (Exception exception) when (exception is not OperationCanceledException)',
        '        catch (Exception exception) when (exception is OperationCanceledException)',
    ),
    mutatie.enkel(
        'M7  zonder eerstelijn komt er toch een escalatiebubbel',
        'Soratus.Portal/Support/SupportDesk.cs',
        '''            logger.LogDebug(
                "Geen eerstelijn aangesloten; de vraag van {CustomerId} wacht op een mens.",
                scope.CustomerId);

            return posted;''',
        '''            logger.LogDebug(
                "Geen eerstelijn aangesloten; de vraag van {CustomerId} wacht op een mens.",
                scope.CustomerId);

            await store.RecordFirstLineAsync(
                scope,
                new SupportEnquiry { Question = question.Text, Grounds = [] },
                answer: null,
                cancellationToken).ConfigureAwait(false);

            return posted;''',
    ),
    mutatie.enkel(
        'M8  SupportAuthor.Customer wordt de standaardwaarde',
        'Soratus.Portal/Support/SupportDocuments.cs',
        '''    [JsonStringEnumMemberName("unknown")]
    Unknown,

    /// <summary>De klant. Vrije tekst die wij later lezen.</summary>
    [JsonStringEnumMemberName("klant")]
    Customer,''',
        '''    /// <summary>De klant. Vrije tekst die wij later lezen.</summary>
    [JsonStringEnumMemberName("klant")]
    Customer,

    [JsonStringEnumMemberName("unknown")]
    Unknown,''',
    ),
    mutatie.enkel(
        'M9  de projectie zet de grondslag voor de escalatie',
        'Soratus.Portal/Support/SupportProjection.cs',
        '''            case SupportAuthor.FirstLine when message.Escalation is not null:
                return new SupportHandoffBubble(message.CreatedAt, text);

            case SupportAuthor.FirstLine
                when message.GroundKind is { } kind''',
        '''            case SupportAuthor.FirstLine
                when message.GroundKind is { } kind''',
    ),
    mutatie.enkel(
        'M10 een antwoord zonder bron valt terug op een escalatiebubbel',
        'Soratus.Portal/Support/SupportProjection.cs',
        '''            default:
                return null;
        }
    }''',
        '''            case SupportAuthor.FirstLine:
                return new SupportHandoffBubble(message.CreatedAt, text);

            default:
                return null;
        }
    }''',
    ),
    mutatie.enkel(
        'M11 een bericht met een onbekende afzender komt toch op het klantscherm',
        'Soratus.Portal/Support/SupportProjection.cs',
        '''        switch (message.Author)
        {
            case SupportAuthor.Customer:''',
        '''        switch (message.Author)
        {
            case SupportAuthor.Unknown:
            case SupportAuthor.Customer:''',
    ),
    mutatie.enkel(
        'M12 SupportBody knipt op de eerste regelovergang (Cut in plaats van Shorten)',
        'Soratus.Portal/Support/SupportBody.cs',
        'return MessageTruncation.Shorten(builder.ToString().Trim(), SupportLimits.MaximumLength);',
        'return MessageTruncation.Cut(builder.ToString().Trim(), SupportLimits.MaximumLength).Message;',
    ),
    mutatie.enkel(
        'M13 de tekens die de leesrichting omkeren blijven staan',
        'Soratus.Portal/Support/SupportBody.cs',
        '''        + "\\u061C\\u200E\\u200F\\u202A\\u202B\\u202C\\u202D\\u202E"
        + "\\u2066\\u2067\\u2068\\u2069"
''',
        '',
    ),
    mutatie.enkel(
        'M14 de documentsleutel draagt geen datum meer',
        'Soratus.Portal/Support/SupportDocuments.cs',
        "$\"{Kind}-{createdAt.UtcDateTime:yyyyMMdd'T'HHmmssfff}-{Convert.ToHexString(digest.AsSpan(0, 4)).ToLowerInvariant()}\");",
        "$\"{Kind}-{createdAt.UtcDateTime:HHmmssfff}-{Convert.ToHexString(digest.AsSpan(0, 4)).ToLowerInvariant()}\");",
    ),
    mutatie.enkel(
        'M15 de constructor van SupportGround wordt publiek',
        'Soratus.Portal/Support/SupportGround.cs',
        '    internal SupportGround(SupportGroundKind kind, string key, string fact)',
        '    public SupportGround(SupportGroundKind kind, string key, string fact)',
    ),
    mutatie.enkel(
        'M16 het klanttype krijgt de escalatieredenen erbij',
        'Soratus.Portal/Support/SupportViews.cs',
        '''    /// <summary>De melding als de draad nog leeg is.</summary>
    public required string EmptyNotice { get; init; }
}

/// <summary>
/// Eén escalatie van de eerstelijn, met de reden. Operator-only.
/// </summary>''',
        '''    /// <summary>De melding als de draad nog leeg is.</summary>
    public required string EmptyNotice { get; init; }

    /// <summary>Gemuteerd: de escalatieredenen op het klanttype.</summary>
    public IReadOnlyList<OperatorHandoff> Handoffs { get; init; } = [];
}

/// <summary>
/// Eén escalatie van de eerstelijn, met de reden. Operator-only.
/// </summary>''',
    ),
    mutatie.enkel(
        'M17 de AI-bubbel verliest zijn bronregel (het merkteken blijft)',
        'Soratus.Portal/Components/Pages/Klant/SupportThread.razor',
        '''                    <p class="support-bubble__ground">
                        <span class="support-bubble__ground-label">@SupportText.GroundIntro</span>
                        <a href="@answer.GroundPath">@answer.GroundLabel</a>
                    </p>
''',
        '',
    ),
    mutatie.enkel(
        'M18 de PageTitle staat buiten de rolcontrole',
        'Soratus.Portal/Components/Pages/Klant/Support.razor',
        '''@if (_operatorView is not null || _customerView is not null)
{
    <PageTitle>Support \u00b7 @_title \u00b7 Agent Portal</PageTitle>
}''',
        '<PageTitle>Support \u00b7 @_title \u00b7 Agent Portal</PageTitle>',
    ),
    mutatie.enkel(
        'M19 de uitweg naar een mens loopt langs de balie',
        'Soratus.Portal/Components/Pages/Klant/Support.razor',
        '        var result = await Store.PostQuestionAsync(_read!, question);',
        '        var result = await Desk.AskAsync(_read!, question);',
    ),
    mutatie.enkel(
        'M20 de operator krijgt de klantweergave (rolvolgorde omgedraaid)',
        'Soratus.Portal/Components/Pages/Klant/Support.razor',
        '''        if (await Scopes.ResolveWriteAsync(user, Slug) is { } write)
        {
            _write = write;
            _actor = write.Actor;
            _title = write.DisplayName;
            await LoadOperatorAsync();
            return;
        }

        if (await Scopes.ResolveAsync(user, Slug) is { } read)''',
        '''        if (await Scopes.ResolveAsync(user, Slug) is { } read0)
        {
            _read = read0;
            _actor = user.Identity?.Name ?? string.Empty;
            await LoadCustomerAsync();
            _title = _customerView!.DisplayName;
            return;
        }

        if (await Scopes.ResolveWriteAsync(user, Slug) is { } write)
        {
            _write = write;
            _actor = write.Actor;
            _title = write.DisplayName;
            await LoadOperatorAsync();
            return;
        }

        if (await Scopes.ResolveAsync(user, Slug) is { } read)''',
    ),
    mutatie.enkel(
        'M21 het schonen slaat de projectie over (alleen bij het schrijven)',
        'Soratus.Portal/Support/SupportProjection.cs',
        '        var text = SupportBody.Clean(message.Text);',
        '        var text = message.Text;',
    ),
    # -- Met opzet stil: mutaties waarvan we vinden dat er geen test op hoort te staan ----------
    mutatie.enkel(
        'S1  de Nederlandse zin van de escalatie wordt anders geformuleerd',
        'Soratus.Portal/Support/SupportText.cs',
        '        "Dit weet ik niet zeker, en dan zeg ik het liever dan dat ik het erbij verzin. Ik heb je "',
        '        "Hmm, daar kom ik niet uit. Ik heb je "',
    ),
    mutatie.enkel(
        'S2  de grens op het aantal aangeboden grondslagen gaat van 60 naar 5',
        'Soratus.Portal/Support/SupportGround.cs',
        '    internal const int Maximum = 60;',
        '    internal const int Maximum = 5;',
    ),
    mutatie.enkel(
        'S3  de paginagrootte gaat van 50 naar 7',
        'Soratus.Portal/Support/SupportEdits.cs',
        '    public const int PageSize = 50;',
        '    public const int PageSize = 7;',
    ),
    mutatie.enkel(
        'S4  de berichtgrens gaat van 4000 naar 400 tekens',
        'Soratus.Portal/Support/SupportBody.cs',
        '    public const int MaximumLength = 4_000;',
        '    public const int MaximumLength = 400;',
    ),
    mutatie.enkel(
        'S5  de klantbubbel wisselt van kant',
        'Soratus.Portal/Components/Pages/Klant/SupportThread.razor.css',
        '.support-bubble--customer {\n  align-self: flex-end;',
        '.support-bubble--customer {\n  align-self: flex-start;',
    ),
    mutatie.enkel(
        'S6  het maxlength-attribuut op het invoerveld verdwijnt',
        'Soratus.Portal/Components/Pages/Klant/Support.razor',
        '''                           Hint="Vragen over de status van je agents, je uren tegen de bundel of een factuur kunnen direct worden beantwoord met de gegevens uit dit portaal."
                           maxlength="@SupportLimits.MaximumLength" />''',
        '''                           Hint="Vragen over de status van je agents, je uren tegen de bundel of een factuur kunnen direct worden beantwoord met de gegevens uit dit portaal." />''',
    ),
    mutatie.enkel(
        'M22 de draad wordt nieuwste-eerst gelezen (Reverse eruit)',
        'Soratus.Portal/Support/CosmosSupportStore.cs',
        '        page.Reverse();',
        '',
    ),]


if __name__ == "__main__":
    raise SystemExit(mutatie.voer_uit(
        MUTATIES,
        filter="FullyQualifiedName~Soratus.Portal.Tests.Support",
        alleen=sys.argv[1:] or None,
    ))
