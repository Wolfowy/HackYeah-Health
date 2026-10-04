# Reguła ekstrakcji `interview_json` dla MVP

Poniższy blok jest opisem pola **String** `interview_json` w Data collection ElevenLabs. Backend normalizuje ten JSON i importuje go do wersji roboczej; pacjent nadal sprawdza treść, zatwierdza i osobno udziela zgody.

```text
Na podstawie wypowiedzi pacjenta w tej rozmowie zwróć WYŁĄCZNIE poprawny JSON obiektu opisanego poniżej, bez Markdown, komentarzy ani dodatkowego tekstu. Wartością pola interview_json ma być string zawierający ten JSON. Wszystkie opisy po polsku. Nie diagnozuj, nie zalecaj leczenia, nie dopisuj faktów. Zachowaj słowa pacjenta, leki i dawki dokładnie jak podano. Jawne korekty pacjenta mają pierwszeństwo; nierozstrzygniętą sprzeczność oznacz stanem Contradictory i opisz w polu tekstowym.

Schemat (wymagane klucze najwyższego poziomu):
{
  "schemaVersion": 1,
  "consultationReason": "powód konsultacji słowami pacjenta lub null",
  "symptoms": [
    {
      "name": "nazwa zgłoszonego objawu",
      "startedOn": null,
      "startedOnState": "Provided",
      "startedOnText": "określenie początku podane przez pacjenta, np. od trzech dni, lub null",
      "frequency": "częstość lub null",
      "severity": null,
      "course": "przebieg lub null",
      "dailyImpact": "wpływ na codzienność lub null",
      "description": "opis objawu lub null",
      "timeline": [{"occurredOn": null, "period": "określenie czasu lub null", "description": "zgłoszone zdarzenie"}]
    }
  ],
  "medications": [{"name": "lek lub suplement", "dose": "dawka lub null", "doseState": "Provided", "schedule": "schemat lub null", "reason": "powód przyjmowania lub null"}],
  "allergies": [{"substance": "alergen", "reaction": "zgłoszona reakcja lub null"}],
  "chronicConditions": [{"name": "choroba zgłoszona przez pacjenta", "description": "opis lub null"}],
  "patientQuestions": ["pytanie pacjenta do lekarza"],
  "additionalNotes": "inne informacje przekazane przez pacjenta lub null",
  "medicationsState": "NotAsked",
  "allergiesState": "NotAsked",
  "chronicConditionsState": "NotAsked"
}

Wartości stanów są wyłącznie: Provided, Unknown, NotAsked, Contradictory.
- Provided: pacjent podał informację albo wyraźnie zaprzeczył, np. nie przyjmuje leków lub nie ma alergii. Przy jawnym zaprzeczeniu odpowiednia tablica jest pusta, a stan listy Provided.
- Unknown: pacjent mówi, że nie wie lub nie pamięta. Nie zamieniaj tego na zaprzeczenie.
- NotAsked: informacja nie została zebrana. Pusta tablica bez wypowiedzi pacjenta wymaga NotAsked.
- Contradictory: dane pozostają sprzeczne po rozmowie.

Każdy zgłoszony objaw i każdy lek ma osobny element tablicy. Nie zwracaj przykładowych elementów, gdy pacjent ich nie podał; wtedy użyj []. Nie twórz elementu z nazwą "brak". severity jest liczbą całkowitą 0–10 wyłącznie jeśli podaną przez pacjenta, inaczej null. startedOn i occurredOn są datami YYYY-MM-DD wyłącznie dla rzeczywiście ustalonej daty, inaczej null. Określenia relatywne zachowaj w startedOnText lub period, bez zgadywania daty. Brak ustalonej chronologii oznacza timeline: []. Dla znanej dawki doseState=Provided; dla "nie pamiętam dawki" doseState=Unknown. Gdy pacjent podał względny początek objawu, startedOnState=Provided nawet jeśli startedOn=null. Wymagane tablice zawsze występują. Nie wyciągaj faktów z pytań lub sugestii agenta.
```

Kontrakt projektu: [normalizacja i import](../Backend/src/DocPrep.Application/Interviews/InterviewExtractionService.cs). Konfiguracja API: [Update agent](https://elevenlabs.io/docs/api-reference/agents/update), [Data collection](https://elevenlabs.io/docs/eleven-agents/customization/agent-analysis/data-collection).
