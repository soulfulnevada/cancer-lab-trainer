# Science, sources, and claim register

## Purpose

Cancer Lab Trainer teaches reasoning around an illustrative plate-based ATP viability workflow. It deliberately does not reproduce a vendor protocol or make biological predictions for real compounds, cell lines, or people.

## Claim register

| Claim | Classification | Evidence or limit |
| --- | --- | --- |
| ATP-dependent luminescence can be used as a viability-associated assay signal. | Documented concept | Promega’s CellTiter-Glo 2.0 technical manual is the source register reference. |
| An ATP-associated signal alone cannot establish cell death, apoptosis, or a mechanism. | Scientific limit | Metabolic state and assay conditions can affect an ATP-related measurement; the app repeats this limitation in results. |
| Controls and replicate wells support interpretable comparisons. | Established experimental reasoning | The app blocks a supported conclusion when a required control is invalid. Guided Practice refuses the conclusion and names the failed checks; Assessment records the attempt for the debrief. |
| A percentage of vehicle is meaningful only when the blank and vehicle reference are valid. | Established experimental reasoning | When the reference fails the model's checks, the app withholds the percentage ("Cannot calculate") in the results, debrief, and downloads instead of showing a number. |
| Paired wells from one preparation are technical replicates; they describe repeatability, not whether a difference holds across independent biological preparations. | Established experimental reasoning | Shown beside the results and replicate summary and asked in a check question. The app has no biological replicates. |
| A luciferase-based signal can be affected by compound interference; independent biological repeats and an orthogonal method strengthen an observation. An orthogonal result alone does not prove mechanism. | Documented concept | NCBI Assay Guidance Manual, "Interferences with Luciferase Reporter Enzymes". Taught through the "What would confirm this?" question. |
| The lytic ATP assay breaks cells open, so a later live-cell observation needs matched wells unless a compatible multiplex order has been validated. | Documented concept | Promega CellTiter-Glo 2.0 technical manual. Taught through a check question. |
| The end-of-level check questions measure learning. | **Not claimed** | They are learning design awaiting qualified review and a learner pilot. Answers are recorded in the attempt and downloads for that purpose. |
| The illustrated forward pipette order starts with first stop in air, then source immersion and release; destination first stop is followed by second stop, withdrawal while held, then release in air. | Documented handling sequence | Eppendorf Research plus operating manual, pp. 22–23. The app labels its quantity bookkeeping as nominal and does not claim calibration, depth, dwell time, or physical error measurement. |
| Accuracy is closeness to an intended target; precision is agreement among repeated transfers. A shared bias can be precise but inaccurate, and repeat ATP-associated signals do not prove volume accuracy. | Documented measurement concepts and general teaching | Eppendorf liquid-handling SOP manual, pp. 23–25, 44–45, 49, 51, 96, 98–99. The app gives one fictional 50-unit/45, 45, 45 example and describes gravimetric checking only as a mass-volume concept using density and appropriate conditions/corrections. It does not reproduce a procedure, tolerance, formula, table, certification, or equipment-performance claim. |
| A fast source release creates a specific physical percent error or calibrated volume bias. | **Not claimed** | It is a qualitative, recoverable training condition. Nominal inventory and fictional readings intentionally remain unchanged. |
| Preparation, safety, records, and closeout are part of laboratory competence. | Documented competency concept | CDC laboratory workforce competency guidelines are the reference. |
| 50 uL transfers, signal readings, thresholds, effects of tip reuse, and fictional treatment response are accurate laboratory values. | **Not claimed** | They are intentionally fictional training values. |
| Successful completion demonstrates physical pipetting competence or predicts an in-lab result. | **Not claimed** | Requires supervised hands-on assessment and, for comparison claims, predefined paired validation. |

## Source register

- Promega. [CellTiter-Glo 2.0 Assay Technical Manual](https://worldwide.promega.com/-/media/files/resources/protocols/technical-manuals/101/celltiterglo-2-0-assay-protocol.pdf?la=en).
- Promega. [MyGlo Reagent Reader](https://www.promega.com/products/cell-health-assays/cell-viability-and-cytotoxicity-assays/myglo-reagent-reader/?catNum=MG1010).
- Eppendorf. [Research plus operating manual](https://www.eppendorf.com/product-media/doc/en/186591/Eppendorf_Liquid-Handling_Operating-manual_Research-plus_Eppendorf-Research-plus.pdf).
- User-supplied reviewed PDF: `Eppendorf_Liquid-Handling_SOP_Manual-pipettes-dispensers_Standard-Operating-Procedure (8).pdf`, edition `ASOP P39 020-16/2025-12`, SHA-256 `D1407722E4953EB11415D2DC80800467995B75E7CC3D4421D4D81DA6DE6848AB`. [Eppendorf's manuals portal](https://www.eppendorf.com/manuals) is the general official portal, not a verified download location for this exact file. The PDF is not bundled or reproduced in the app, or treated as an in-app SOP.
- CDC. [Laboratory workforce competency guidelines](https://www.cdc.gov/mmwr/preview/mmwrhtml/su6002a1.htm).
- Auld DS, Inglese J. [Interferences with Luciferase Reporter Enzymes](https://www.ncbi.nlm.nih.gov/books/NBK374281/). In: Assay Guidance Manual, NCBI Bookshelf.

The visual design, interaction flow, outcome model, and fictional data are original to this project. Source material informs broad concepts only.
