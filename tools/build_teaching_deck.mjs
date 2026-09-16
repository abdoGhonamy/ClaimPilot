import fs from "node:fs/promises";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { Presentation, PresentationFile } from "@oai/artifact-tool";

const skillDir = "/home/abdelbadea/.codex/plugins/cache/openai-primary-runtime/presentations/26.909.22227/skills/presentations";
const workspace = "/home/abdelbadea/API/Insurance_claims_adjudication/InsuranceClaimsCopilot";
const tmp = path.join(workspace, ".build/teaching-deck");
const output = path.join(workspace, "teaching/ClaimPilot-Trustworthy-RAG.pptx");
const { resolvePresentationFont, finalizePresentation } = await import(pathToFileURL(path.join(skillDir, "container_tools/artifact_tool_utils.mjs")).href);
await fs.mkdir(tmp, { recursive: true });
const font = resolvePresentationFont();
const deck = Presentation.create({ slideSize: { width: 1280, height: 720 } });
const slides = [
  ["Trustworthy insurance RAG", "A 90-minute postgraduate session using ClaimPilot\n\nGrounded retrieval, deterministic decisions, and human review"],
  ["Learning outcomes", "Explain why retrieval must be version-aware\nTrace a grounded answer to a policy chunk\nDistinguish model drafting from deterministic money logic\nOperate an auditable approval gate"],
  ["The claims decision problem", "A claim is not answered by the newest policy wording.\n\nThe incident date chooses the applicable version.\nThe decision also needs limits, deductibles, exclusions, and a human accountable for the outcome."],
  ["The ClaimPilot workflow", "Claim intake\nVersion-aware policy retrieval\nCoverage and exclusion analysis\nDeterministic payout calculation\nDraft rationale\nHuman review and final decision"],
  ["Grounded answers", "Every answer must cite the supporting chunk.\n\nLow evidence is not a reason to guess.\nThe correct response can be: Not enough information in the policy corpus to determine this."],
  ["Structural chunks", "Policy chunks retain policy number, version, section, clause, and page.\n\nThis supports citations that a reviewer can verify and metadata filtering before generation."],
  ["Hybrid retrieval", "Dense search helps with paraphrases.\nKeyword search helps with exact insurance terminology.\nFusion improves recall, while version filtering prevents a newer wording from contaminating an older claim."],
  ["The version trap", "AUT-2022 incident on 2023-03-10\nClaim amount: $6,200\nApplicable wording: version 1\nLimit: $5,000\n\nA current wording would be wrong evidence."],
  ["Agent boundaries", "Coverage Matcher retrieves the version and coverage items.\nExclusion Analyst proposes candidates and verifies evidence.\nAnomaly Detector records review signals.\nAdjudication Drafter writes rationale only."],
  ["Deterministic money logic", "The deterministic engine applies deductible, coinsurance, limit, and exclusions.\n\nThe model cannot change the amount.\nThis makes the calculation testable and repeatable."],
  ["Human review queue", "A draft enters a persisted queue.\nAssignment depends on priority and amount.\nApprovers must match the assigned role and authority threshold.\nApprove, reject, edit, escalation, and SLA actions are audited."],
  ["Observability", "The SSE stream shows agent and tool progress.\nA run trace records policy version, tools, chunks, computation steps, anomalies, usage, cost, and approval history.\nCorrelation IDs link the evidence."],
  ["OWASP LLM controls", "Retrieved documents are data, never instructions.\nTool allow-lists restrict agency.\nModel output does not become HTML, shell, SQL, or a file path without validation.\nPrompt injection cases remain in the evaluation set."],
  ["Lab activity", "Run the version-trap claim.\nInspect the trace and identify the selected policy version.\nAsk an unsupported question and verify refusal.\nUse a supervisor account to inspect the review queue."],
  ["Assessment and next steps", "Evidence: trace explanation, version-trap test, safe refusal, and approval decision.\n\nStretch: add a new policy version, adversarial wording, or authority threshold test.\n\nSource: ClaimPilot repository teaching pack."],
];
for (let i = 0; i < slides.length; i++) {
  const [titleText, bodyText] = slides[i]; const slide = deck.slides.add(); slide.background.fill = i === 0 ? "#103B4C" : "#F8FAFC";
  const title = slide.shapes.add({ geometry:"textbox", position:{left:80,top:62,width:1120,height:80}, fill:"none", line:{fill:"none",width:0} });
  title.text = titleText; title.text.style = { typeface:font,fontSize:i===0?50:38,bold:true,color:i===0?"#FFFFFF":"#103B4C",autoFit:"shrinkText" };
  const body = slide.shapes.add({ geometry:"textbox", position:{left:110,top:190,width:1040,height:390}, fill:"none", line:{fill:"none",width:0} });
  body.text = bodyText; body.text.style = { typeface:font,fontSize:27,color:i===0?"#E6F4F1":"#263642",breakLine:true,autoFit:"shrinkText",paragraphSpacing:12 };
  const footer = slide.shapes.add({ geometry:"textbox", position:{left:80,top:655,width:1120,height:24}, fill:"none", line:{fill:"none",width:0} });
  footer.text = `ClaimPilot teaching pack  |  ${i + 1}`; footer.text.style = { typeface:font,fontSize:13,color:i===0?"#B9D5D1":"#60727F",autoFit:"shrinkText" };
  slide.speakerNotes.textFrame.setText("Teaching deck based on the implemented ClaimPilot repository and assignment requirements.");
}
const candidate = path.join(tmp, "candidate.pptx");
await (await PresentationFile.exportPptx(deck)).save(candidate);
await finalizePresentation({ workspaceDir:workspace,candidatePath:candidate,finalPath:output,pythonExecutable:"/home/abdelbadea/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/bin/python3",integrityValidatorPath:path.join(skillDir,"container_tools/inspect_presentation_package_integrity.py"),layoutValidatorPath:path.join(skillDir,"container_tools/inspect_presentation_layout_geometry.py"),layoutArgs:["--expected-slide-size-emu","12192000,6858000","--validate-bullet-geometry","--validate-heading-fit"],fontPolicy:{basis:"design",families:[font]},verifyArtifactToolImport:true,receiptPath:path.join(tmp,"validation.json") });
