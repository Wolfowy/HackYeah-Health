# DocPrep

System pomaga pacjentowi uporządkować informacje przed wizytą i udostępnić lekarzowi zatwierdzony raport. Nie diagnozuje, nie zaleca leczenia i nie ocenia pilności objawów.

## Uruchomienie lokalne — cały system w Dockerze

Potrzebny jest uruchomiony Docker Desktop. Polecenia wykonaj w głównym katalogu projektu:

```sh
cp -n Backend/.env.example Backend/.env
docker compose --env-file Backend/.env up --build -d
```

Przed uruchomieniem uzupełnij `Backend/.env`: hasło bazy oraz klucz, identyfikator agenta i sekret webhooka ElevenLabs. Istniejący plik `.env` zostanie zachowany. Rozmowa i automatyczny raport wymagają poprawnej konfiguracji ElevenLabs oraz publicznego adresu webhooka — [instrukcja integracji](docs/elevenlabs-uruchomienie.md).

- Panel recepcji i lekarza: <http://127.0.0.1:5174>
- Front pacjenta: <http://127.0.0.1:5173>
- API: <http://127.0.0.1:8080>
- Lokalne konta: `admin@docprep.local`, `doctor@docprep.local`; hasło: `DocPrepDemo!2026`.

Compose uruchamia API, PostgreSQL, Redis i oba fronty. Fronty mają gotowy build z Nginx i przekazują `/api` do backendu. Dane bazy pozostają w wolumenach po zatrzymaniu:

```sh
docker compose --env-file Backend/.env logs -f api
docker compose --env-file Backend/.env down
```

Jeśli porty zajmuje wcześniejsze uruchomienie projektu, zatrzymaj je albo wybierz inne porty:

```sh
DOCPREP_API_PORT=8180 DOCPREP_PATIENT_PORT=5183 DOCPREP_PANEL_PORT=5184 FRONTEND_BASE_URL=http://127.0.0.1:5183 docker compose --env-file Backend/.env up --build -d
```

Domyślnie jest to lokalne demo w trybie `Development`. Obrazy można wdrożyć z tego samego Compose; konfiguracja produkcyjna wymaga `ASPNETCORE_ENVIRONMENT=Production`, własnych sekretów JWT/szyfrowania i adresu frontu pacjenta w `Backend/.env`, publicznego HTTPS oraz konfiguracji kont zgodnie z [dokumentacją backendu](Backend/README.md). `DOCPREP_BIND_ADDRESS` pozwala zmienić domyślne powiązanie portów z `127.0.0.1`.

## Dokumentacja

- [Dokumentacja backendu](Backend/README.md)
- [Uruchomienie frontendu](Frontend/README.md)
- [Panel recepcji i lekarza — API, demo i uruchomienie](AdminFrontend/README.md)
- [Opis ekranów i modeli frontendu](docs/przed-wizyta-frontend.md)
- [Konfiguracja ElevenLabs i stan integracji](docs/elevenlabs-uruchomienie.md)
- [Plan implementacji backendu i frontendu](docs/plan-implementacji-backend-frontend.md)
- [Karta projektu](docs/DocPrep%20-%20karta%20projektu.md)
- [Dokumentacja procesów](docs/DocPrep%20-%20dokumentacja%20procesów.md)
