# Native research results — not production acceptance

Variant: two separate anonymous ordinary-card strata, capacity 2048, 149M research Work, unchanged Standard Storage, Debug native. Timing includes solver preparation but excludes worker transport/input capture; samples are single serial observations, not cold/warm browser measurements. Every displayed lower endpoint is rounded down, upper and full width up to six decimal percentage digits. Exact integer endpoints remain in anonymous-results.jsonl.

**The practical target is not achieved.** Neither original heavy input reaches the initial 2pp width goal; the hand-size-six challenge remains much wider. No production route, public result contract or AOT/browser claim is enabled. Frozen heavy references are regression checks, not an independent mathematical proof.

| Input | Work | Native ms | Peak cells / structure | Aggregate cells estimate | Allocated bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| danger-control-40 | 36786 | 61.67 | 5206 | 11444 | 282856 |
| danger-single-40 | 53520 | 3.24 | 5398 | 11897 | 341800 |
| danger-mixed-40 | 357893 | 9.98 | 6196 | 14079 | 1435272 |
| danger-heavy-40 | 148999998 | 1542.45 | 124355 | 230331 | 643012320 |
| danger-control-60 | 59957 | 0.70 | 8822 | 20396 | 420400 |
| danger-single-60 | 81898 | 0.61 | 9014 | 20880 | 487120 |
| danger-mixed-60 | 527035 | 2.79 | 9812 | 23456 | 1651008 |
| danger-heavy-60 | 148999951 | 1145.59 | 141518 | 253353 | 555884408 |
| challenge-heavy-40-hand6 | 148999965 | 1337.25 | 71171 | 181769 | 619891664 |
| challenge-heavy-60-priority | 148999996 | 1153.10 | 197727 | 296871 | 517383792 |
| challenge-heavy-40-multiplicity | 148999959 | 1446.84 | 189472 | 279178 | 634229464 |
| challenge-heavy-60-direct | 148999998 | 1171.72 | 128103 | 239752 | 559944200 |
| challenge-mixed-40-direct-exclusion | 344745 | 3.71 | 5987 | 13689 | 1352376 |

Allocation totals are not live retained heap; payload estimates exclude object overhead. The first control sample includes native startup/JIT effects. No universal deadline follows from these timings.

## danger-control-40

Source SHA256: `D18C42A883A1BA823F2593BF9231817DCB9A3150B75DDD019782183DB3E6A441`. Stop: exhausted retained frontier.

| Row | Lower % | Upper % | Full width pp |
| --- | ---: | ---: | ---: |
| Any active combo | 75.248933 | 75.248934 | 0.000000 |
| Starter and extender | 52.343436 | 52.343437 | 0.000000 |
| Overlap | 34.522376 | 34.522377 | 0.000000 |
| Extender without starter | 17.368785 | 17.368786 | 0.000000 |
| Positive routes | 57.880147 | 57.880148 | 0.000000 |
| Exclusion route | 17.368785 | 17.368786 | 0.000000 |

## danger-single-40

Source SHA256: `D18C42A883A1BA823F2593BF9231817DCB9A3150B75DDD019782183DB3E6A441`. Stop: exhausted retained frontier.

| Row | Lower % | Upper % | Full width pp |
| --- | ---: | ---: | ---: |
| Any active combo | 74.177973 | 74.177974 | 0.000000 |
| Starter and extender | 50.662909 | 50.662910 | 0.000000 |
| Overlap | 33.755060 | 33.755061 | 0.000000 |
| Extender without starter | 17.747656 | 17.747657 | 0.000000 |
| Positive routes | 56.430316 | 56.430317 | 0.000000 |
| Exclusion route | 17.747656 | 17.747657 | 0.000000 |

## danger-mixed-40

Source SHA256: `D18C42A883A1BA823F2593BF9231817DCB9A3150B75DDD019782183DB3E6A441`. Stop: exhausted retained frontier.

| Row | Lower % | Upper % | Full width pp |
| --- | ---: | ---: | ---: |
| Any active combo | 79.344227 | 79.344228 | 0.000000 |
| Starter and extender | 58.395162 | 58.395163 | 0.000000 |
| Overlap | 43.594193 | 43.594194 | 0.000000 |
| Extender without starter | 17.375308 | 17.375309 | 0.000000 |
| Positive routes | 64.554501 | 64.554502 | 0.000000 |
| Exclusion route | 17.375308 | 17.375309 | 0.000000 |

## danger-heavy-40

Source SHA256: `D18C42A883A1BA823F2593BF9231817DCB9A3150B75DDD019782183DB3E6A441`. Stop: Work.

| Row | Lower % | Upper % | Full width pp |
| --- | ---: | ---: | ---: |
| Any active combo | 78.516345 | 85.266548 | 6.750202 |
| Starter and extender | 63.193157 | 69.943360 | 6.750202 |
| Overlap | 49.052977 | 55.803180 | 6.750202 |
| Extender without starter | 15.550748 | 22.300951 | 6.750202 |
| Positive routes | 67.809525 | 74.559727 | 6.750202 |
| Exclusion route | 15.550748 | 22.300951 | 6.750202 |

## danger-control-60

Source SHA256: `D18C42A883A1BA823F2593BF9231817DCB9A3150B75DDD019782183DB3E6A441`. Stop: exhausted retained frontier.

| Row | Lower % | Upper % | Full width pp |
| --- | ---: | ---: | ---: |
| Any active combo | 57.678852 | 57.678853 | 0.000000 |
| Starter and extender | 28.915489 | 28.915490 | 0.000000 |
| Overlap | 23.701128 | 23.701129 | 0.000000 |
| Extender without starter | 20.628693 | 20.628694 | 0.000000 |
| Positive routes | 37.050159 | 37.050160 | 0.000000 |
| Exclusion route | 20.628693 | 20.628694 | 0.000000 |

## danger-single-60

Source SHA256: `D18C42A883A1BA823F2593BF9231817DCB9A3150B75DDD019782183DB3E6A441`. Stop: exhausted retained frontier.

| Row | Lower % | Upper % | Full width pp |
| --- | ---: | ---: | ---: |
| Any active combo | 56.988833 | 56.988834 | 0.000000 |
| Starter and extender | 28.163739 | 28.163740 | 0.000000 |
| Overlap | 23.334307 | 23.334308 | 0.000000 |
| Extender without starter | 20.640822 | 20.640823 | 0.000000 |
| Positive routes | 36.348011 | 36.348012 | 0.000000 |
| Exclusion route | 20.640822 | 20.640823 | 0.000000 |

## danger-mixed-60

Source SHA256: `D18C42A883A1BA823F2593BF9231817DCB9A3150B75DDD019782183DB3E6A441`. Stop: exhausted retained frontier.

| Row | Lower % | Upper % | Full width pp |
| --- | ---: | ---: | ---: |
| Any active combo | 61.898971 | 61.898972 | 0.000000 |
| Starter and extender | 33.177271 | 33.177272 | 0.000000 |
| Overlap | 30.506595 | 30.506596 | 0.000000 |
| Extender without starter | 20.627469 | 20.627470 | 0.000000 |
| Positive routes | 43.037963 | 43.037964 | 0.000000 |
| Exclusion route | 20.627469 | 20.627470 | 0.000000 |

## danger-heavy-60

Source SHA256: `D18C42A883A1BA823F2593BF9231817DCB9A3150B75DDD019782183DB3E6A441`. Stop: Work.

| Row | Lower % | Upper % | Full width pp |
| --- | ---: | ---: | ---: |
| Any active combo | 64.152548 | 66.290640 | 2.138092 |
| Starter and extender | 37.826717 | 39.964810 | 2.138092 |
| Overlap | 35.653517 | 37.791610 | 2.138092 |
| Extender without starter | 20.042991 | 22.181084 | 2.138092 |
| Positive routes | 47.692079 | 49.830172 | 2.138092 |
| Exclusion route | 20.042991 | 22.181084 | 2.138092 |

## challenge-heavy-40-hand6

Source SHA256: `D18C42A883A1BA823F2593BF9231817DCB9A3150B75DDD019782183DB3E6A441`. Stop: Work.

| Row | Lower % | Upper % | Full width pp |
| --- | ---: | ---: | ---: |
| Any active combo | 72.656653 | 91.556673 | 18.900020 |
| Starter and extender | 63.402039 | 82.302059 | 18.900020 |
| Overlap | 48.518502 | 67.418522 | 18.900020 |
| Extender without starter | 10.861491 | 29.761511 | 18.900020 |
| Positive routes | 65.846834 | 84.746855 | 18.900020 |
| Exclusion route | 10.861491 | 29.761511 | 18.900020 |

## challenge-heavy-60-priority

Source SHA256: `D18C42A883A1BA823F2593BF9231817DCB9A3150B75DDD019782183DB3E6A441`. Stop: Work.

| Row | Lower % | Upper % | Full width pp |
| --- | ---: | ---: | ---: |
| Any active combo | 65.737078 | 66.856417 | 1.119339 |
| Starter and extender | 39.098925 | 40.218265 | 1.119339 |
| Overlap | 36.710741 | 37.830081 | 1.119339 |
| Extender without starter | 20.284044 | 21.403383 | 1.119339 |
| Positive routes | 49.115199 | 50.234539 | 1.119339 |
| Exclusion route | 20.284044 | 21.403383 | 1.119339 |

## challenge-heavy-40-multiplicity

Source SHA256: `D18C42A883A1BA823F2593BF9231817DCB9A3150B75DDD019782183DB3E6A441`. Stop: Work.

| Row | Lower % | Upper % | Full width pp |
| --- | ---: | ---: | ---: |
| Any active combo | 80.998203 | 84.497118 | 3.498915 |
| Starter and extender | 65.081065 | 68.579980 | 3.498915 |
| Overlap | 50.320372 | 53.819287 | 3.498915 |
| Extender without starter | 16.422169 | 19.921084 | 3.498915 |
| Positive routes | 69.765869 | 73.264784 | 3.498915 |
| Exclusion route | 16.422169 | 19.921084 | 3.498915 |

## challenge-heavy-60-direct

Source SHA256: `D18C42A883A1BA823F2593BF9231817DCB9A3150B75DDD019782183DB3E6A441`. Stop: Work.

| Row | Lower % | Upper % | Full width pp |
| --- | ---: | ---: | ---: |
| Any active combo | 52.129891 | 54.240911 | 2.111020 |
| Starter and extender | 5.583633 | 7.694653 | 2.111020 |
| Overlap | 35.664290 | 37.775311 | 2.111020 |
| Extender without starter | 20.050025 | 22.161045 | 2.111020 |
| Positive routes | 35.664290 | 37.775311 | 2.111020 |
| Exclusion route | 20.050025 | 22.161045 | 2.111020 |

## challenge-mixed-40-direct-exclusion

Source SHA256: `D18C42A883A1BA823F2593BF9231817DCB9A3150B75DDD019782183DB3E6A441`. Stop: exhausted retained frontier.

| Row | Lower % | Upper % | Full width pp |
| --- | ---: | ---: | ---: |
| Any active combo | 58.383919 | 58.383920 | 0.000000 |
| Starter and extender | 14.905395 | 14.905396 | 0.000000 |
| Overlap | 43.594193 | 43.594194 | 0.000000 |
| Extender without starter | 17.375308 | 17.375309 | 0.000000 |
| Positive routes | 43.594193 | 43.594194 | 0.000000 |
| Exclusion route | 17.375308 | 17.375309 | 0.000000 |
