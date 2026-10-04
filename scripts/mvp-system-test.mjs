import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { chromium } from "../Frontend/node_modules/playwright/index.mjs";

// Live acceptance test: no application API routes or provider transport are mocked.
// Uses Development staff accounts and leaves one clearly named fictional visit for inspection.
if (!process.argv.includes("--live"))
  throw new Error(
    "Podaj --live: test tworzy fikcyjną wizytę i rzeczywistą rozmowę tekstową ElevenLabs.",
  );
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const panelUrl = process.env.MVP_PANEL_URL || "http://127.0.0.1:5174";
const patientUrl = process.env.MVP_PATIENT_URL || "http://127.0.0.1:5173";
const apiUrl = process.env.MVP_API_URL || "http://127.0.0.1:8080";
const browser = await chromium.launch({ channel: "chrome", headless: true });
const context = await browser.newContext({
  timezoneId: "Europe/Warsaw",
  viewport: { width: 1440, height: 1050 },
});
const evidenceDir = fs.mkdtempSync("/private/tmp/docprep-mvp-live-");
fs.chmodSync(evidenceDir, 0o700);
const evidence = {
  provider: "ElevenLabs live text",
  api: apiUrl,
  screenshots: evidenceDir,
  steps: [],
};
const resumeIndex = process.argv.indexOf("--resume");
if (resumeIndex >= 0) {
  Object.assign(
    evidence,
    JSON.parse(fs.readFileSync(process.argv[resumeIndex + 1], "utf8")),
    { screenshots: evidenceDir },
  );
  assert.ok(evidence.visitId && evidence.versionId && evidence.patientName);
  evidence.resumeFrom = process.argv[resumeIndex + 1];
  delete evidence.error;
}
function done(step) {
  evidence.steps.push(step);
  console.log(step);
}
async function signIn(page, email) {
  await page.goto(panelUrl);
  await page.getByLabel("Adres e-mail", { exact: true }).fill(email);
  await page.getByLabel("Hasło", { exact: true }).fill("DocPrepDemo!2026");
  await page.getByRole("button", { name: "Zaloguj się", exact: true }).click();
  await page
    .getByRole("button", { name: /Odśwież/ })
    .first()
    .waitFor({ timeout: 20000 });
}
async function staffAccess(email) {
  const response = await fetch(apiUrl + "/api/v1/auth/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email, password: "DocPrepDemo!2026" }),
  });
  assert.equal(response.status, 200);
  return (await response.json()).accessToken;
}
async function existingInvitation() {
  const accessToken = await staffAccess("admin@docprep.local");
  const response = await fetch(
    apiUrl + `/api/v1/admin/appointments/${evidence.visitId}/invitation`,
    {
      headers: { Authorization: `Bearer ${accessToken}` },
    },
  );
  assert.equal(response.status, 200);
  return (await response.json()).url;
}
async function reportHash(versionId, accessToken) {
  const response = await fetch(
    apiUrl +
      `/api/v1/integration/visits/${evidence.visitId}/report-versions/${versionId}`,
    {
      headers: { Authorization: `Bearer ${accessToken}` },
    },
  );
  assert.equal(response.status, 200);
  const { report } = await response.json();
  return createHash("sha256").update(JSON.stringify(report)).digest("hex");
}
async function approveWholeReport(patient) {
  assert.equal(
    await patient
      .getByRole("button", { name: "Akceptuję obserwację", exact: true })
      .count(),
    0,
  );
  assert.equal(
    await patient
      .getByRole("button", { name: "Pobierz JSON", exact: true })
      .count(),
    0,
  );
  assert.equal(
    await patient.getByRole("button", { name: /^Udostępnij wersję/ }).count(),
    0,
  );
  const mutations = [];
  const capture = (request) => {
    if (["POST", "PUT"].includes(request.method()))
      mutations.push(new URL(request.url()).pathname);
  };
  patient.on("request", capture);
  const approveResponse = patient.waitForResponse((response) =>
    response.url().endsWith("/api/v1/interview/approve"),
  );
  await patient
    .getByRole("button", { name: "Zatwierdź i udostępnij raport", exact: true })
    .click();
  const approved = await approveResponse;
  assert.equal(approved.status(), 200);
  assert.equal(approved.request().postDataJSON().acceptAllObservations, true);
  assert.equal(approved.request().postDataJSON().shareWithFacility, true);
  const version = await approved.json();
  evidence.versionId = version.versionId;
  evidence.versionNumber = version.versionNumber;
  await patient
    .getByRole("button", { name: /Cofnij.*udostępnienie/ })
    .waitFor();
  patient.off("request", capture);
  assert.equal(mutations.filter((path) => path.endsWith("/approve")).length, 1);
  assert.ok(
    !mutations.some(
      (path) => path.endsWith("/decision") || path.endsWith("/consent"),
    ),
  );
  await patient.screenshot({
    path: path.join(evidenceDir, "03-patient-shared.png"),
    fullPage: true,
  });
  done("4. Jeden przycisk zatwierdził cały raport i udostępnił go lekarzowi.");
}
try {
  if (process.argv.includes("--revise-report")) {
    assert.ok(resumeIndex >= 0, "Edycja istniejącego raportu wymaga --resume.");
    assert.ok(
      evidence.patientName.startsWith("Pacjent MVP Test "),
      "Ten wariant zmienia tylko fikcyjną wizytę MVP.",
    );
    evidence.steps = evidence.steps.filter(
      (step) => !/^(4\.|5\.|PDF istniejącej)/.test(step),
    );
    const clinician = await staffAccess("doctor@docprep.local");
    evidence.previousVersionId = evidence.versionId;
    evidence.previousReportSha256 = await reportHash(
      evidence.previousVersionId,
      clinician,
    );
    const patient = await context.newPage();
    await patient.goto(await existingInvitation());
    await patient.locator(".report-review").waitFor();
    // Start with no sharing so the new single approval must grant it itself.
    const revoke = patient.getByRole("button", {
      name: /Cofnij.*udostępnienie/,
    });
    if (await revoke.isVisible()) {
      const revoked = patient.waitForResponse(
        (response) =>
          response.url().endsWith("/api/v1/interview/consent") &&
          response.request().method() === "PUT",
      );
      await revoke.click();
      assert.equal((await revoked).status(), 204);
      await revoke.waitFor({ state: "hidden" });
    }
    await patient
      .getByRole("button", { name: /Edytuj lek.*paracetamol/i })
      .click();
    await patient
      .getByRole("textbox", { name: /^Lek \d+$/ })
      .fill("Paracetamol testowy");
    await patient
      .getByRole("textbox", { name: "Dawka", exact: true })
      .fill("500 mg");
    await patient
      .getByRole("button", { name: "Zapisz poprawki", exact: true })
      .click();
    await patient
      .getByRole("heading", { name: "Paracetamol testowy", exact: true })
      .waitFor();
    await patient
      .getByRole("button", { name: "Edytuj: Alergie", exact: true })
      .click();
    if (
      !(await patient.getByRole("textbox", { name: /^Alergen \d+$/ }).count())
    )
      await patient
        .getByRole("button", { name: "Dodaj alergię", exact: true })
        .click();
    await patient
      .getByRole("textbox", { name: /^Alergen \d+$/ })
      .first()
      .fill("Pyłki traw — test");
    await patient
      .getByRole("textbox", { name: "Reakcja", exact: true })
      .first()
      .fill("Kichanie — dane testowe");
    await patient
      .getByRole("button", { name: "Zapisz poprawki", exact: true })
      .click();
    await patient
      .getByText("Pyłki traw — test", { exact: false })
      .first()
      .waitFor();
    evidence.revisedFields = {
      medication: "Paracetamol testowy",
      allergen: "Pyłki traw — test",
    };
    await approveWholeReport(patient);
    evidence.previousSnapshotUnchanged =
      (await reportHash(evidence.previousVersionId, clinician)) ===
      evidence.previousReportSha256;
    assert.equal(evidence.previousSnapshotUnchanged, true);
    done(
      "Nazwę leku i alergen poprawiono w UI; poprzednia zatwierdzona wersja jest niezmienna.",
    );
  }
  if (process.argv.includes("--regenerate-pdf")) {
    assert.ok(resumeIndex >= 0, "Ponowne generowanie PDF wymaga --resume.");
    const login = await fetch(apiUrl + "/api/v1/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        email: "admin@docprep.local",
        password: "DocPrepDemo!2026",
      }),
    });
    assert.equal(login.status, 200);
    const admin = await login.json();
    const invitationResponse = await fetch(
      apiUrl + `/api/v1/admin/appointments/${evidence.visitId}/invitation`,
      {
        headers: { Authorization: `Bearer ${admin.accessToken}` },
      },
    );
    assert.equal(invitationResponse.status, 200);
    const invitation = await invitationResponse.json();
    fs.mkdirSync(path.join(root, "Backend/.local"), { recursive: true });
    fs.writeFileSync(
      path.join(root, "Backend/.local/mvp-live-link.txt"),
      invitation.url + "\n",
      { mode: 0o600 },
    );
    const token = new URL(invitation.url).pathname.split("/").pop();
    const authorization = await fetch(
      apiUrl + `/api/public/interviews/${encodeURIComponent(token)}/authorize`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: "{}",
      },
    );
    assert.equal(authorization.status, 200);
    const patient = await authorization.json();
    const repaired = await fetch(
      apiUrl + "/api/v1/interview/report/regenerate-pdf",
      {
        method: "POST",
        headers: { Authorization: `Bearer ${patient.accessToken}` },
      },
    );
    assert.equal(repaired.status, 204);
    done("PDF istniejącej wersji wygenerowany ponownie przez API.");
  }
  if (resumeIndex < 0) {
    const login = await fetch(apiUrl + "/api/v1/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        email: "admin@docprep.local",
        password: "DocPrepDemo!2026",
      }),
    });
    assert.equal(login.status, 200);
    const admin = await login.json();
    const doctors = await fetch(apiUrl + "/api/v1/admin/doctors", {
      headers: { Authorization: `Bearer ${admin.accessToken}` },
    }).then((r) => r.json());
    const assignedDoctor = doctors.find(
      (doctor) => doctor.id === "doctor-demo",
    );
    assert.ok(
      assignedDoctor,
      "Brak lekarza przypisanego do konta doctor@docprep.local.",
    );
    const existing = await fetch(
      apiUrl + "/api/v1/admin/appointments?page=1&pageSize=100",
      { headers: { Authorization: `Bearer ${admin.accessToken}` } },
    ).then((r) => r.json());
    const now = new Date();
    const day = new Intl.DateTimeFormat("en-CA", {
      timeZone: "Europe/Warsaw",
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
    }).format(now);
    // Select a free future slot today, so the clinician's default daily queue includes this visit.
    const slot = Array.from(
      { length: 22 },
      (_, i) =>
        new Date(
          `${day}T${String(12 + Math.floor(i / 2)).padStart(2, "0")}:${i % 2 ? "30" : "00"}:00+02:00`,
        ),
    ).find(
      (date) =>
        date > now &&
        !(existing.items || []).some(
          (v) =>
            v.doctor?.id === "doctor-demo" &&
            Date.parse(v.scheduledAt) < date.getTime() + 30 * 60000 &&
            Date.parse(v.endsAt) > date.getTime(),
        ),
    );
    assert.ok(
      slot,
      "Brak wolnego slotu dzisiaj — ustaw test na właściwy przyszły dzień.",
    );
    const time = new Intl.DateTimeFormat("en-GB", {
      timeZone: "Europe/Warsaw",
      hour: "2-digit",
      minute: "2-digit",
      hour12: false,
    }).format(slot);
    const name =
      "Pacjent MVP Test " + now.toISOString().slice(11, 19).replaceAll(":", "");
    evidence.patientName = name;
    const reception = await context.newPage();
    await signIn(reception, "admin@docprep.local");
    await reception.getByRole("button", { name: /Nowa wizyta/ }).click();
    await reception.getByLabel("Imię i nazwisko pacjenta").fill(name);
    await reception
      .getByLabel("E-mail", { exact: true })
      .fill("mvp@example.invalid");
    await reception.getByLabel("Lekarz", { exact: true }).click();
    await reception
      .getByTitle(`${assignedDoctor.name} · ${assignedDoctor.specialty}`, {
        exact: true,
      })
      .click();
    await reception.getByLabel("Gabinet", { exact: true }).fill("MVP-test");
    await reception.getByLabel("Godzina", { exact: true }).fill(time);
    await reception.getByLabel("Godzina", { exact: true }).press("Tab");
    const createdResponse = reception.waitForResponse(
      (r) =>
        r.url().endsWith("/api/v1/admin/appointments") &&
        r.request().method() === "POST",
    );
    await reception
      .getByRole("button", { name: "Dodaj wizytę", exact: true })
      .click();
    const response = await createdResponse;
    assert.equal(response.status(), 201);
    const created = await response.json();
    evidence.visitId = created.appointment.visitId;
    evidence.interviewId = created.appointment.interviewId;
    assert.ok(created.invitation.url.startsWith(patientUrl + "/i/"));
    fs.mkdirSync(path.join(root, "Backend/.local"), { recursive: true });
    fs.writeFileSync(
      path.join(root, "Backend/.local/mvp-live-link.txt"),
      created.invitation.url + "\n",
      { mode: 0o600 },
    );
    await reception.getByRole("heading", { name, exact: true }).waitFor();
    await reception.screenshot({
      path: path.join(evidenceDir, "01-reception.png"),
      fullPage: true,
    });
    done("1. Wizyta utworzona przez panel i rzeczywiste API; otrzymano link.");

    const patient = await context.newPage();
    const errors = [];
    patient.on("pageerror", (error) => errors.push(error.message));
    await patient.goto(created.invitation.url);
    await patient
      .getByRole("region", { name: "Informacje o wizycie", exact: true })
      .waitFor();
    assert.equal(new URL(patient.url()).pathname, "/rozmowa");
    await patient.getByRole("button", { name: "Czat", exact: true }).click();
    await patient
      .getByRole("button", { name: "Rozpocznij czat", exact: true })
      .click();
    const answer = patient.getByRole("textbox", {
      name: "Twoja odpowiedź",
      exact: true,
    });
    await answer.waitFor({ timeout: 30000 });
    await patient
      .locator(".assistant .chat-bubble")
      .first()
      .waitFor({ timeout: 30000 });
    done("2. Link wymieniony na sesję; rzeczywisty czat ElevenLabs połączony.");
    const replies = [
      "To fikcyjne dane do testu aplikacji. Powodem wizyty jest ból głowy i kaszel. Ból głowy rozpoczął się 1 października 2026, wraca raz dziennie, ma nasilenie 4 na 10 i utrudnia koncentrację. Kaszel zaczął się 2 października 2026, występuje wieczorami, ma nasilenie 2 na 10 i trochę przeszkadza w zasypianiu. Przyjmuję paracetamol 500 mg doraźnie z powodu bólu oraz witaminę D 1000 jednostek raz dziennie jako suplement. Nie mam alergii ani chorób przewlekłych. Moje pytanie do lekarza: co warto omówić podczas wizyty? Dodatkowo chcę powiedzieć, że wczoraj oba objawy były słabsze.",
      "Dane podane wyżej są kompletne. Ból głowy dotyczy okolicy czoła, a kaszel jest suchy. Nie zgłaszam innych objawów. Nie pamiętam innych szczegółów. To wszystkie informacje, które chcę przekazać.",
      "Nie chcę już nic dodawać. Proszę podsumować przekazane informacje i zakończyć wywiad.",
    ];
    for (const text of replies) {
      if (!(await answer.isVisible())) break;
      const previous = await patient.locator(".assistant .chat-bubble").count();
      await answer.fill(text);
      await patient
        .getByRole("button", { name: "Wyślij odpowiedź", exact: true })
        .click();
      await patient.waitForFunction(
        (count) =>
          document.querySelectorAll(".assistant .chat-bubble").length > count ||
          !!document.querySelector(".report-review"),
        previous,
        { timeout: 30000 },
      );
    }
    const end = patient.getByRole("button", {
      name: "Zakończ rozmowę",
      exact: true,
    });
    if (await end.isVisible()) await end.click();
    await patient.locator(".report-review").waitFor({ timeout: 90000 });
    await patient
      .getByRole("heading", { name: "Powód wizyty", exact: true })
      .waitFor();
    assert.equal(errors.length, 0, errors.join("\n"));
    const text = await patient.locator(".report-review").innerText();
    assert.match(text, /głow/i);
    assert.match(text, /kaszel/i);
    assert.match(text, /paracetamol/i);
    assert.ok(!text.includes("Nie udało się w pełni przenieść rozmowy"));
    await patient.screenshot({
      path: path.join(evidenceDir, "02-patient-review.png"),
      fullPage: true,
    });
    done(
      "3. Dostawca zakończył rozmowę; backend zaimportował objawy i leki do draftu.",
    );
    await approveWholeReport(patient);
  }

  const doctor = await context.newPage();
  await signIn(doctor, "doctor@docprep.local");
  await doctor
    .getByRole("button", {
      name: `Otwórz raport ${evidence.patientName}`,
      exact: true,
    })
    .click();
  await doctor.locator(".report-content").waitFor();
  const content = await doctor.locator(".report-content").innerText();
  assert.match(content, /głow/i);
  assert.match(content, /kaszel/i);
  assert.match(content, /paracetamol/i);
  if (evidence.revisedFields) {
    assert.ok(content.includes(evidence.revisedFields.medication));
    assert.ok(content.includes(evidence.revisedFields.allergen));
  }
  const pdfResponse = doctor.waitForResponse((r) =>
    r.url().includes(`/report-versions/${evidence.versionId}/pdf`),
  );
  const downloadEvent = doctor.waitForEvent("download");
  await doctor.getByRole("button", { name: /Pobierz PDF/ }).click();
  const pdf = await pdfResponse;
  assert.equal(pdf.status(), 200);
  const pdfPath = path.join(evidenceDir, "doctor-report.pdf");
  await (await downloadEvent).saveAs(pdfPath);
  const bytes = fs.readFileSync(pdfPath);
  assert.equal(bytes.subarray(0, 4).toString(), "%PDF");
  const pdfText = execFileSync("pdftotext", [pdfPath, "-"], {
    encoding: "utf8",
  });
  assert.match(pdfText, /głow/i);
  assert.match(pdfText, /kaszel/i);
  assert.match(pdfText, /paracetamol/i);
  assert.ok(pdfText.includes(evidence.versionId));
  if (evidence.revisedFields) {
    assert.ok(pdfText.includes(evidence.revisedFields.medication));
    assert.ok(pdfText.includes(evidence.revisedFields.allergen));
  }
  evidence.pdfBytes = bytes.length;
  await doctor.screenshot({
    path: path.join(evidenceDir, "04-doctor-report.png"),
    fullPage: true,
  });
  done("5. Przypisany lekarz odczytał zatwierdzony raport i PDF przez API.");
  fs.writeFileSync(
    path.join(evidenceDir, "result.json"),
    JSON.stringify(evidence, null, 2),
    { mode: 0o600 },
  );
  console.log(JSON.stringify(evidence, null, 2));
} catch (error) {
  evidence.error = error.message;
  for (const [index, page] of context.pages().entries()) {
    await page
      .screenshot({
        path: path.join(evidenceDir, `failure-${index}.png`),
        fullPage: true,
      })
      .catch(() => {});
    fs.writeFileSync(
      path.join(evidenceDir, `failure-${index}.txt`),
      await page
        .locator("body")
        .innerText()
        .catch(() => ""),
      { mode: 0o600 },
    );
  }
  fs.writeFileSync(
    path.join(evidenceDir, "result.json"),
    JSON.stringify(evidence, null, 2),
    { mode: 0o600 },
  );
  console.error(JSON.stringify({ error: error.message, evidenceDir }));
  process.exitCode = 1;
} finally {
  await browser.close();
}
