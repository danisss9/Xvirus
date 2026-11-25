### load_first

| Product | Load time (ms) | CPU time (ms) | Host baseline WS (MB) | WS after load (MB) | Engine footprint (MB) | Peak WS (MB) | Peak private (MB) |
|---|--:|--:|--:|--:|--:|--:|--:|
| C# SDK | 850 | 953 | 23.8 | 435 | 412 | 449 | 424 |
| Native SDK | 667 | 781 | 3.7 | 417 | 413 | 430 | 417 |
| Node.js SDK | 655 | 719 | 34.6 | 446 | 411 | 459 | 440 |
| CLI | 865 | 953 | 21.4 | 434 | 412 | 447 | 424 |

### load_steady

| Product | Load time (ms) | CPU time (ms) | Host baseline WS (MB) | WS after load (MB) | Engine footprint (MB) | Peak WS (MB) | Peak private (MB) |
|---|--:|--:|--:|--:|--:|--:|--:|
| C# SDK | 851 | 922 | 23.9 | 437 | 413 | 449 | 424 |
| Native SDK | 647 | 719 | 3.7 | 417 | 413 | 430 | 417 |
| Node.js SDK | 655 | 734 | 34.6 | 446 | 411 | 460 | 441 |
| CLI | 865 | 984 | 21.4 | 433 | 412 | 447 | 424 |

### load_by_profile

| Engine profile | C# SDK (ms) | Native SDK (ms) | Node.js SDK (ms) | CLI (ms) | Engine footprint (MB) |
|---|--:|--:|--:|--:|--:|
| Full (Sig+Heur+AI) | 839 | 638 | 643 | 855 | 413 |
| Sig+Heuristics | 773 | 579 | 575 | 785 | 380 |
| Sig+AI | 810 | 614 | 614 | 813 | 397 |
| Signatures only | 736 | 556 | 551 | 754 | 363 |
| Full + AI Script | 1,815 | 1,516 | 1,592 | 1,818 | 1,659 |

### pe_by_sdk

| File size | C# SDK (ms) | Native SDK (ms) | Node.js SDK (ms) | CLI (ms) | Native MB/s | CPU ms / scan (C#) |
|---|--:|--:|--:|--:|--:|--:|
| 16 KB | 3.62 | 3.74 | 3.80 | 4.03 | 4.2 | 26.8 |
| 128 KB | 6.96 | 7.06 | 7.16 | 6.93 | 17.7 | 40.2 |
| 512 KB | 17.02 | 19.03 | 18.13 | 16.89 | 26.3 | 105 |
| 2 MB | 60.78 | 64.36 | 63.26 | 60.76 | 31.1 | 138 |
| 8 MB | 216 | 223 | 226 | 215 | 35.8 | 354 |
| 18 MB | 487 | 504 | 513 | 494 | 35.7 | 620 |
| 32 MB | 103 | 102 | 103 | 102 | 313 | 120 |

### text_by_sdk

| File size | C# SDK (ms) | Native SDK (ms) | Node.js SDK (ms) | CLI (ms) | Native MB/s | CPU ms / scan (C#) |
|---|--:|--:|--:|--:|--:|--:|
| 4 KB | 1.86 | 2.26 | 2.22 | 1.94 | 1.7 | 2.2 |
| 64 KB | 2.64 | 3.32 | 3.03 | 2.74 | 18.8 | 0.0 |
| 512 KB | 8.32 | 9.24 | 9.53 | 8.74 | 54.1 | 6.7 |
| 2 MB | 28.19 | 31.02 | 31.13 | 28.98 | 64.5 | 29.0 |
| 8 MB | 105 | 119 | 120 | 106 | 67.2 | 106 |
| 32 MB | 79.93 | 80.25 | 80.83 | 79.95 | 399 | 78.1 |

### binary_by_sdk

| File size | C# SDK (ms) | Native SDK (ms) | Node.js SDK (ms) | CLI (ms) | Native MB/s | CPU ms / scan (C#) |
|---|--:|--:|--:|--:|--:|--:|
| 4 KB | 2.29 | 2.58 | 2.59 | 2.32 | 1.5 | 2.2 |
| 64 KB | 3.60 | 4.23 | 4.05 | 3.89 | 14.8 | 4.5 |
| 512 KB | 16.08 | 17.19 | 16.86 | 16.12 | 29.1 | 17.9 |
| 2 MB | 58.01 | 61.26 | 60.97 | 58.39 | 32.6 | 60.3 |
| 8 MB | 228 | 238 | 240 | 228 | 33.6 | 231 |
| 32 MB | 82.60 | 89.59 | 82.99 | 82.26 | 357 | 78.1 |

### pe_by_engine

| File size | Full (Sig+Heur+AI) (ms) | Sig+Heuristics (ms) | Sig+AI (ms) | Signatures only (ms) |
|---|--:|--:|--:|--:|
| 16 KB | 3.74 | 1.14 | 3.24 | 0.81 |
| 128 KB | 7.06 | 4.02 | 3.78 | 1.22 |
| 512 KB | 19.03 | 14.23 | 5.29 | 2.31 |
| 2 MB | 64.36 | 57.72 | 13.06 | 7.26 |
| 8 MB | 223 | 210 | 38.18 | 26.48 |
| 18 MB | 504 | 488 | 81.37 | 60.56 |
| 32 MB | 102 | 109 | 103 | 102 |

### text_by_engine

| File size | Full (Sig+Heur+AI) (ms) | Sig+Heuristics (ms) | Sig+AI (ms) | Signatures only (ms) |
|---|--:|--:|--:|--:|
| 4 KB | 2.26 | 2.22 | 2.37 | 2.40 |
| 64 KB | 3.32 | 3.26 | 2.23 | 2.23 |
| 512 KB | 9.24 | 9.47 | 3.45 | 3.34 |
| 2 MB | 31.02 | 31.10 | 7.31 | 7.31 |
| 8 MB | 119 | 118 | 22.93 | 23.57 |
| 32 MB | 80.25 | 81.17 | 83.38 | 81.98 |

### binary_by_engine

| File size | Full (Sig+Heur+AI) (ms) | Sig+Heuristics (ms) | Sig+AI (ms) | Signatures only (ms) |
|---|--:|--:|--:|--:|
| 4 KB | 2.58 | 2.53 | 2.42 | 2.45 |
| 64 KB | 4.23 | 3.98 | 2.30 | 2.20 |
| 512 KB | 17.19 | 17.10 | 3.30 | 3.33 |
| 2 MB | 61.26 | 60.85 | 7.07 | 7.02 |
| 8 MB | 238 | 239 | 23.24 | 23.55 |
| 32 MB | 89.59 | 85.44 | 85.06 | 83.77 |

### first_vs_warm

| File | C# SDK first / warm (ms) | Native SDK first / warm (ms) | Node.js SDK first / warm (ms) | CLI first / warm (ms) |
|---|--:|--:|--:|--:|
| pe_2MB.dll | 63.3 / 60.78 | 64.4 / 64.36 | 64.4 / 63.26 | 62.0 / 60.76 |
| text_64KB.log | 2.8 / 2.64 | 3.1 / 3.32 | 3.5 / 3.03 | 2.8 / 2.74 |
| signed_pe.exe | 16.3 / 3.31 | 15.6 / 3.34 | 15.5 / 3.33 | 16.5 / 3.29 |

### signed

| Product | Signed PE 163 KB (ms) | Unsigned PE 128 KB (ms) | Verdict |
|---|--:|--:|--:|
| C# SDK | 3.31 | 6.96 | Safe |
| Native SDK | 3.34 | 7.06 | Safe |
| Node.js SDK | 3.33 | 7.16 | Safe |
| CLI | 3.29 | 6.93 | Safe |

### folder_full

| Product | Time (ms) | Files/s | MB/s | CPU (% of one core) | WS after (MB) | Peak WS (MB) |
|---|--:|--:|--:|--:|--:|--:|
| C# SDK | 4,292 | 233 | 26.9 | 374 | 807 | 816 |
| Native SDK | 5,017 | 199 | 23.0 | 339 | 811 | 837 |
| Node.js SDK | 5,143 | 194 | 22.4 | 334 | 835 | 861 |
| CLI | 4,299 | 233 | 26.9 | 367 | 806 | 816 |

### folder_heur

| Product | Time (ms) | Files/s | MB/s | CPU (% of one core) | WS after (MB) | Peak WS (MB) |
|---|--:|--:|--:|--:|--:|--:|
| C# SDK | 3,702 | 270 | 31.2 | 88.6 | 700 | 772 |
| Native SDK | 4,296 | 233 | 26.9 | 88.7 | 679 | 750 |
| Node.js SDK | 4,327 | 231 | 26.7 | 87.4 | 707 | 776 |
| CLI | 3,724 | 269 | 31.0 | 89.8 | 696 | 769 |

### folder_ai

| Product | Time (ms) | Files/s | MB/s | CPU (% of one core) | WS after (MB) | Peak WS (MB) |
|---|--:|--:|--:|--:|--:|--:|
| C# SDK | 1,789 | 559 | 64.5 | 387 | 777 | 788 |
| Native SDK | 2,233 | 448 | 51.7 | 330 | 774 | 779 |
| Node.js SDK | 2,150 | 465 | 53.7 | 315 | 808 | 813 |
| CLI | 1,750 | 571 | 66.0 | 377 | 800 | 805 |

### folder_sig

| Product | Time (ms) | Files/s | MB/s | CPU (% of one core) | WS after (MB) | Peak WS (MB) |
|---|--:|--:|--:|--:|--:|--:|
| C# SDK | 1,208 | 828 | 95.6 | 78.9 | 634 | 683 |
| Native SDK | 1,621 | 617 | 71.2 | 76.2 | 612 | 664 |
| Node.js SDK | 1,633 | 612 | 70.7 | 77.5 | 645 | 694 |
| CLI | 1,218 | 821 | 94.8 | 79.6 | 633 | 682 |

### folder_summary

| Engine profile | C# SDK files/s | Native SDK files/s | Node.js SDK files/s | CLI files/s | CPU % core (Native) |
|---|--:|--:|--:|--:|--:|
| Full (Sig+Heur+AI) | 233 | 199 | 194 | 233 | 339 |
| Sig+Heuristics | 270 | 233 | 231 | 269 | 88.7 |
| Sig+AI | 559 | 448 | 465 | 571 | 330 |
| Signatures only | 828 | 617 | 612 | 821 | 76.2 |

### update

| Product | First check (ms) | Median check (ms) | CPU (ms) | WS delta (MB) | Result |
|---|--:|--:|--:|--:|--:|
| C# SDK | 248 | 183 | 15.6 | -3.82 | Database is up-to-date! |
| Native SDK | 204 | 182 | 0.0 | 0.01 | Database is up-to-date! |
| Node.js SDK | 203 | 174 | 0.0 | 0.02 | Database is up-to-date! |
| CLI | 249 | 183 | 15.6 | 0.55 | Database is up-to-date! |

### fresh_update

| Run | Product | Time (ms) | CPU (ms) | Peak WS (MB) | Result |
|---|--:|--:|--:|--:|--:|
| fresh_update_full_aiscript | C# SDK | 23,557 | 1,641 | 45.9 | Database was updated! |
| fresh_update_default_NativeSDK | Native SDK | 2,692 | 125 | 20.8 | Database was updated! |
| fresh_update_default_NodeSDK | Node.js SDK | 3,373 | 94.0 | 50.8 | Database was updated! |

### aiscript

| File | C# SDK (ms) | Native SDK (ms) | Node.js SDK (ms) | CLI (ms) |
|---|--:|--:|--:|--:|
| backup.ps1 | 947 | 910 | 843 | 875 |
| build.bat | 703 | 716 | 655 | 695 |
| server.js | 973 | 998 | 918 | 967 |
| report.py | 772 | 758 | 759 | 789 |
| pe_2MB.dll | 60.4 | 65.1 | 65.0 | 61.7 |
| text_2MB.log | 28.2 | 31.4 | 31.7 | 29.1 |

### aiscript_mem

| Product | Load (ms) | WS after load (MB) | Peak WS (MB) | Peak private (MB) |
|---|--:|--:|--:|--:|
| C# SDK | 1,815 | 1,683 | 2,217 | 1,570 |
| Native SDK | 1,516 | 1,661 | 2,190 | 1,567 |
| Node.js SDK | 1,592 | 1,689 | 2,218 | 1,584 |
| CLI | 1,818 | 1,683 | 2,216 | 1,576 |

### cli_oneshot

| Command | Wall time (ms) | CPU (ms) | Peak WS (MB) | Peak private (MB) |
|---|--:|--:|--:|--:|
| version | 34.9 | 31.2 | 22.0 | 6.0 |
| load | 857 | 906 | 447 | 424 |
| scan_pe_512KB | 933 | 1,125 | 450 | 424 |
| scan_pe_2MB | 976 | 1,141 | 451 | 428 |
| scan_text_64KB | 879 | 969 | 447 | 424 |
| scan_signed_pe | 891 | 953 | 446 | 423 |
| update | 325 | 156 | 41.3 | 11.0 |

### scaling

| Threads | Time (ms) | Files/s | MB/s | Speed-up | CPU (% of one core) | WS (MB) |
|---|--:|--:|--:|--:|--:|--:|
| 1 | 4,353 | 230 | 26.5 | 1.00× | 373 | 526 |
| 2 | 2,485 | 402 | 46.5 | 1.75× | 527 | 586 |
| 4 | 1,541 | 649 | 74.9 | 2.82× | 736 | 634 |
| 6 | 1,290 | 775 | 89.5 | 3.38× | 824 | 565 |
| 8 | 1,082 | 924 | 107 | 4.02× | 963 | 596 |
| 12 | 1,013 | 987 | 114 | 4.30× | 1,002 | 635 |

### peak_by_profile

| Engine profile | C# SDK peak WS (MB) | Native SDK peak WS (MB) | Node.js SDK peak WS (MB) | CLI peak WS (MB) |
|---|--:|--:|--:|--:|
| Full (Sig+Heur+AI) | 816 | 837 | 861 | 816 |
| Sig+Heuristics | 772 | 750 | 776 | 769 |
| Sig+AI | 788 | 779 | 813 | 805 |
| Signatures only | 683 | 664 | 694 | 682 |
| Full + AI Script | 2,217 | 2,190 | 2,218 | 2,216 |
