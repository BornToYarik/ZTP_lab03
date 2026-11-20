# Przewodnik do Prezentacji i Obrony Projektu (ZTP — Lab 03)

Ten dokument zawiera gotowy scenariusz prezentacji projektu dla prowadzącego w języku polskim, wyjaśnienie każdego zastosowanego wzorca oraz dokładne wskazówki, które pliki i funkcje w interfejsie webowym (`http://localhost:5100`) pokazać dla każdego kryterium oceny.

---

## 1. Podsumowanie Architektury i Użytych Technologii (Co powiedzieć na wstępie)

> **Co powiedzieć prowadzącemu:**
> *"Projekt został zrealizowany na platformie .NET 9 zgodnie z zasadami **Clean Architecture** (Czystej Architektury), wzorcem **CQRS** (Command Query Responsibility Segregation), wzorcem **Mediator**, podejściem **Event Sourcing** do zapewnienia spójności operacyjnej koszyka na wielu urządzeniach oraz bazą danych **PostgreSQL** uruchomioną w kontenerze **Docker**."*

### Podział na moduły i warstwy (10 projektów w `ZTP_lab03.sln`):
1. **`ZtpLab03.SharedKernel`**:
   * Wspólne abstrakcje wzorca **CQRS** (`ICommand`, `ICommandHandler`, `IQuery`, `IQueryHandler`), implementacja wzorca **Mediator** (`IMediator`, `Mediator`), klasa `Result<T>` (zasada DRY) oraz zdarzenie integracyjne `CartCheckedOutIntegrationEvent`.
2. **Domena Koszyka (`Cart`) — podzielona na 3 warstwy Clean Architecture**:
   * **Warstwa 1: `ZtpLab03.Cart.Domain`** — Agregat `ShoppingCart` odtwarzany ze strumienia zdarzeń (**Event Sourcing**), zdarzenia domenowe (`CartCreatedEvent`, `ProductAddedToCartEvent`, `ProductRemovedFromCartEvent`, `CartCheckedOutEvent`, `CartExpiredEvent`) oraz porty (`ICartEventStore`, `IProductServiceClient`).
   * **Warstwa 2: `ZtpLab03.Cart.Application`** — Podzielona na pojedyncze **Use Case'y** (każdy jako osobna Komenda lub Zapytanie CQRS), w pełni testowalne bez uruchamiania serwera HTTP.
   * **Warstwa 3: `ZtpLab03.Cart.Infrastructure`** — `PostgresCartEventStore` (Event Store w bazie PostgreSQL z tabelą `cart_events` i kolumną `JSONB`), `HttpProductServiceClient` (komunikacja z serwisem produktowym przez protokół HTTP) oraz `CartExpirationBackgroundService` (automatyczne wygaszanie blokad po 15 minutach).
3. **Domena Zamówień (`Orders`) — całkowicie niezależna i odseparowana od Koszyka (+0.25)**:
   * **Warstwa 1: `ZtpLab03.Orders.Domain`** — Encja `Order`, `OrderStatus`, `OrderNotification`, porty `IOrderRepository` i `IOrderNotificationService`.
   * **Warstwa 2: `ZtpLab03.Orders.Application`** — Use Case'y: `StartOrderProcessingCommand` (reaguje na zdarzenie zatwierdzenia koszyka, tworzy zamówienie i wysyła powiadomienie użytkownikowi), `GetUserOrdersQuery`, `GetUserNotificationsQuery`.
   * **Warstwa 3: `ZtpLab03.Orders.Infrastructure`** — `PostgresOrderStore` zapisujący zamówienia i powiadomienia w osobnych tabelach PostgreSQL (`orders`, `order_notifications`).
4. **Serwis Produktowy (`ZtpLab03.Products`)**:
   * Odpowiada za katalog produktów oraz atomową blokadę stanów magazynowych (`AvailableStock` oraz `ReservedStock`), udostępnianą przez endpointy HTTP (`/api/products/...`).
5. **Host Web API i Panel Prezentacyjny (`ZtpLab03.Api`)**:
   * Cienkie kontrolery REST (`CartsController`, `OrdersController`, `ProductsController`) oraz interaktywny dashboard webowy pod adresem `http://localhost:5100`.

---

## 2. Jak obronić każde kryterium oceny (Pytania prowadzącego + Odpowiedzi)

### Kryterium na 3.0:
#### A. Wzorzec CQRS i Mediator
* **Gdzie pokazać w kodzie**:
  * `src/Shared/ZtpLab03.SharedKernel/CQRS/CqrsAbstractions.cs` oraz `Mediator.cs`
  * `src/Cart/ZtpLab03.Cart.Application/UseCases/...`
* **Jak wytłumaczyć**:
  > *"Zgodnie z wzorcem CQRS rozdzieliliśmy operacje modyfikujące stan od operacji odczytujących. Każda modyfikacja koszyka (utworzenie, dodanie produktu, usunięcie produktu, zatwierdzenie koszyka, wygaszenie po 15 min) jest osobną **Komendą** (`ICommand`), a odczyt zawartości i łącznej wartości koszyka jest **Zapytaniem** (`IQuery` -> `GetCartQuery`). Kontrolery nie zawierają logiki biznesowej — przekazują komendy i zapytania przez wzorzec **Mediator** (`IMediator`) do odpowiedniego Use Case Handlera."*

#### B. Spójność operacyjna (Wiele urządzeń jednocześnie) i Event Sourcing
* **Gdzie pokazać w kodzie**:
  * `src/Cart/ZtpLab03.Cart.Domain/Aggregates/ShoppingCart.cs` (metoda `Rehydrate`)
  * `src/Cart/ZtpLab03.Cart.Infrastructure/EventStore/PostgresCartEventStore.cs` (oraz komentarze dokumentacyjne w `InMemoryCartEventStore.cs`)
* **Jak wytłumaczyć**:
  > *"Gdy ten sam użytkownik dodaje i usuwa produkty równocześnie z telefonu i laptopa, klasyczne podejście CRUD (odczyt całego wiersza koszyka i jego nadpisanie) prowadzi do problemu **Lost Update** (jedna operacja nadpisuje drugą). W naszym rozwiązaniu zastosowaliśmy **Event Sourcing**: nigdy nie nadpisujemy stanu koszyka, lecz dopisujemy niemutowalne zdarzenia (`ProductAddedToCartEvent`, `ProductRemovedFromCartEvent`) do tabeli `cart_events` w PostgreSQL. Wewnątrz `ExecuteInCartLockAsync` stosujemy transakcyjną blokadę doradczą PostgreSQL (`pg_advisory_xact_lock`) per `CartId` oraz ograniczenie `UNIQUE (cart_id, version)`. Dzięki temu równoległe żądania dla tego samego koszyka są szeregowane bez utraty żadnej operacji, a aktualny stan koszyka odtwarzamy metodą `ShoppingCart.Rehydrate(events)`."*

#### C. Skalowalność pozioma oraz Współbieżność i Izolacja użytkowników
* **Jak wytłumaczyć**:
  > *"Handlery Use Case'ów oraz kontrolery API są całkowicie **bezstanowe (stateless)**, a cały stan znajduje się we współdzielonym Event Storze w PostgreSQL. Pozwala to na uruchomienie wielu instancji serwisu koszyków za load balancerem (skalowanie poziome). Każdy użytkownik jest identyfikowany unikalnym identyfikatorem (`X-User-Id`), a agregat `ShoppingCart` sprawdza metodą `IsOwnedBy(userId)`, czy operację wykonuje właściciel koszyka (w przeciwnym razie zwracany jest kod `403 Forbidden`). Blokady w bazie są zakładane wyłącznie dla konkretnego `CartId`, więc tysiące użytkowników mogą zarządzać swoimi koszykami równolegle bez wzajemnego blokowania."*

#### D. Komunikacja z serwisem produktowym przez protokół HTTP
* **Gdzie pokazać w kodzie**:
  * `src/Cart/ZtpLab03.Cart.Infrastructure/Http/HttpProductServiceClient.cs`
* **Jak wytłumaczyć**:
  > *"Domena koszyka komunikuje się z serwisem produktowym wyłącznie przez protokół HTTP przy użyciu `HttpClient` (`POST /api/products/{id}/reserve`, `POST /api/products/{id}/release`, `POST /api/products/reservations/{cartId}/confirm`)."*

---

### Kryterium na +0.5: Blokada produktu na 15 minut oraz Analiza: Domena Koszyka vs Domena Produktów
* **Gdzie pokazać w kodzie**:
  * `src/Products/ZtpLab03.Products/Services/ProductCatalogService.cs` (pełna analiza w komentarzach XML na górze klasy!)
  * `src/Cart/ZtpLab03.Cart.Infrastructure/BackgroundServices/CartExpirationBackgroundService.cs`
  * `src/Cart/ZtpLab03.Cart.Application/UseCases/ExpireInactiveCarts/ExpireInactiveCartsUseCase.cs`
* **Jak odpowiedzieć na pytanie o analizę zalet i wad blokady w domenie koszyka vs domenie produktów**:
  > *"Przeprowadziliśmy analizę obu opcji (udokumentowaną w `ProductCatalogService.cs` oraz `ARCHITECTURE_AND_CHANGELOG.md`):*
  > *1. **Blokada w domenie koszyka**:*
  >    * *Zaleta*: brak konieczności wykonywania wywołań sieciowych HTTP przy sprawdzaniu blokady i wygaszaniu koszyka po 15 minutach.*
  >    * *Wady*: łamie zasadę **Single Source of Truth** (koszyk musiałby duplikować wiedzę o stanach magazynowych z domeny produktów); utrudnia skalowanie poziome, ponieważ przy rezerwacji ostatniej sztuki produktu trzeba by blokować i przeszukiwać wszystkie aktywne koszyki wszystkich użytkowników.*
  > *2. **Blokada w domenie produktów skoordynowana z 15-minutowym czasem życia koszyka (Wybrane rozwiązanie)**:*
  >    * *Zalety*: Serwis Produktowy jest jedynym źródłem prawdy o stanach magazynowych (`AvailableStock` i `ReservedStock`). Operacja rezerwacji `TryReserveProduct` atomowo zmniejsza `AvailableStock` i zwiększa `ReservedStock` dla danego `ProductId`. Pozwala to na bezkonfliktowe skalowanie poziome wielu instancji koszyka.*
  >    * *Wada*: wymaga wywołania HTTP przy zwalnianiu blokady po 15 minutach.*
  > *Dlatego wybraliśmy **blokadę stanu w domenie produktów**, natomiast czas bezczynności koszyka (15 minut) śledzi domena koszyka (`CartExpirationBackgroundService` wywołujący `ExpireInactiveCartsCommand`), która po upływie 15 minut dopisuje `CartExpiredEvent` i zwalnia zarezerwowane sztuki przez HTTP."*

---

### Kryterium na +0.25 (Pierwsze): Rozdzielenie domeny zamówień i koszyka + Powiadomienie użytkownika
* **Gdzie pokazać w kodzie**:
  * Foldery `src/Cart/...` oraz `src/Orders/...`
  * `src/Orders/ZtpLab03.Orders.Application/UseCases/StartOrderProcessing/StartOrderProcessingUseCase.cs`
* **Jak wytłumaczyć**:
  > *"Projekty `ZtpLab03.Cart.*` i `ZtpLab03.Orders.*` nie posiadają żadnych bezpośrednich referencji do siebie. Komunikują się asynchronicznie przez zdarzenie integracyjne `CartCheckedOutIntegrationEvent` publikowane przez Mediator w momencie zatwierdzenia koszyka (`CheckoutCartCommand`). Handler w domenie zamówień tworzy niezależny agregat `Order` w statusie `Processing` i za pomocą `IOrderNotificationService` wysyła użytkownikowi powiadomienie o rozpoczęciu realizacji zamówienia."*

---

### Kryterium na +0.25 (Drugie): Podział na Use Case'y, brak powtórzeń (DRY) i testowalność bez uruchamiania serwera
* **Gdzie pokazać w kodzie**:
  * `tests/ZtpLab03.UnitTests/UseCasesTests.cs`
* **Jak wytłumaczyć**:
  > *"Każdy przypadek użycia (Use Case) ma własny folder i klasę Handlera w warstwie Application, która zależy wyłącznie od abstrakcyjnych interfejsów (portów). W projekcie `ZtpLab03.UnitTests` znajduje się 5 testów jednostkowych xUnit, które testują bezpośrednio wszystkie Use Case'y (w tym współbieżny dostęp z wielu urządzeń i wygaśnięcie blokady po 15 minutach) w ułamku sekundy, bez uruchamiania serwera ASP.NET Core."*

---

## 3. Scenariusz Pokazu na Żywo (Krok po kroku w przeglądarce `http://localhost:5100`)

1. **Uruchomienie bazy PostgreSQL i aplikacji**:
   ```powershell
   docker compose up -d
   dotnet run --project src/ZtpLab03.Api
   ```
2. **Otwórz w przeglądarce**: `http://localhost:5100`
3. **Krok 1 — Utworzenie koszyka i blokada ostatniej sztuki produktu (+0.5)**:
   * Jako użytkownik `user-yarik-01` kliknij **"+ Dodaj 1 szt."** przy produkcie **NVIDIA RTX 5090 (Last Unit! — 1 szt.)**.
   * Pokaż prowadzącemu:
     * W Panelu 1 stan `Dostępne` spadł do **0 szt.**, a `Zablokowane` wzrosło do **🔒 1 szt.**
     * W Panelu 3 (Event Sourcing) pojawiły się zdarzenia `v1 • CartCreatedEvent` oraz `v2 • ProductAddedToCartEvent`.
4. **Krok 2 — Pokaz izolacji użytkowników i działania blokady**:
   * W prawym górnym rogu przełącz użytkownika na `user-anna-02`.
   * Spróbuj kliknąć **"+ Dodaj 1 szt."** przy tej samej karcie **NVIDIA RTX 5090** — pojawi się czerwony komunikat błędu **HTTP 409 Conflict: Insufficient available stock** (produkt jest zablokowany przez pierwszego użytkownika!).
   * Dodatkowo możesz wkleić ID koszyka pierwszego użytkownika będąc zalogowanym jako `user-anna-02` — system zwróci **HTTP 403 Forbidden** (pełna izolacja koszyków).
5. **Krok 3 — Pokaz wygaśnięcia blokady po 15 minutach (+0.5)**:
   * Przełącz z powrotem na `user-yarik-01` i kliknij czerwony przycisk **"⏱️ Symuluj upływ 15 min (Wygaszenie blokady)"**.
   * Pokaż prowadzącemu:
     * Koszyk otrzymał zdarzenie `CartExpiredEvent` i został zamknięty.
     * W Panelu 1 karta **NVIDIA RTX 5090** ponownie ma **1 szt. Dostępne** i **0 szt. Zablokowane**!
6. **Krok 4 — Pokaz spójności operacyjnej z wielu urządzeń jednocześnie (Event Sourcing)**:
   * Kliknij **"➕ Utwórz nowy koszyk"**, a następnie żółty przycisk **"⚡ Symuluj 3 urządzenia naraz (Spójność operacyjna)"**.
   * Przeglądarka wyśle równolegle (`Promise.all`) serię żądań dodawania i usuwania produktów.
   * W Panelu 3 pokaż, że wszystkie zdarzenia (`v1`..`v7`) zostały uporządkowane sekwencyjnie w strumieniu Event Sourcing w PostgreSQL i żadna operacja nie została utracona.
7. **Krok 5 — Finalizacja koszyka (Checkout) i powiadomienie z Domeny Zamówień (+0.25)**:
   * Kliknij zielony przycisk **"✅ Zatwierdź koszyk (Checkout)"**.
   * W Panelu 4 pokaż nowo utworzone zamówienie w statusie `Processing` oraz powiadomienie użytkownika: *"Your order '...' has entered 'Processing' status and is now being fulfilled."*

---

## 4. O co chodzi z prezentacją na +1.0 punkt?

W PDF na stronie 3 widnieje zapis:
> **Ocena +1.0**: *"Przygotowanie 15-minutowej prezentacji, w trakcie której zostanie zaprezentowane demo technologiczne wybranej technologii związanej z tematyką zajęć ZTP. Temat prezentacji musi zostać wcześniej skonsultowany i zaakceptowany przez prowadzącego."*

### Co to oznacza w praktyce?
To nie jest zwykłe pokazanie swojego kodu z zadania (za sam kod zadania z wszystkimi bonusami dostajesz do **4.0**: `3.0 + 0.5 + 0.25 + 0.25`), tylko **dodatkowe 15-minutowe wystąpienie (slajdy + krótkie live demo)** dla grupy na zajęciach o jednej konkretnej technologii lub bibliotece powiązanej z tematyką przedmiotu (CQRS, Event Sourcing, Architektura, Kolejki komunikatów, Testowanie itp.), żeby wskoczyć z oceny **4.0 na 5.0**.

### 3 gotowe propozycje tematów, które możesz wysłać prowadzącemu do akceptacji (i które mamy już praktycznie gotowe w tym projekcie!):
1. **"Event Sourcing i CQRS w praktyce z wykorzystaniem PostgreSQL (JSONB) oraz bibliotek Marten / MediatR w .NET"** — możesz wykorzystać nasz gotowy projekt i pokazać na żywo, jak strumień zdarzeń rozwiązuje problem współbieżnej edycji danych z wielu urządzeń.
2. **"Wzorzec Mediator oraz pipeline'y zachowań (Validation, Logging, Transaction) w architekturze Clean Architecture (.NET)"** — bardzo prosty i przyjemny temat na 15 minut.
3. **"Komunikacja asynchroniczna między domenami i wzorzec Outbox w architekturze modularnego monolitu"** — nawiązuje bezpośrednio do rozdzielenia domeny Koszyka i Zamówień z zadania.
