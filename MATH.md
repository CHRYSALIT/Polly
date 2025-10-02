## Probability of Connecting to All Servers
All this section is IA generated and not verified.

**Assistant:**  
This is the classic *coupon collector / occupancy* problem. I’ll assume every connection attempt independently picks one of the $n$ servers **uniformly at random** (if your servers are not equally likely, skip to the [“non-uniform” paragraph](#if-servers-are-not-equally-likely)).

---

### Probability that after $m$ attempts every server was contacted at least once
Number of all possible sequences of $m$ choices: $$n^m$$

Number of sequences that hit **every** server at least once = number of onto functions from an $m$-set to an $n$-set, which equals

$$
n! \, S(m,n)
$$

where $S(m,n)$ is the Stirling number of the second kind. So

$$
P(\text{all $n$ seen after $m$ attempts}) = \frac{n! \, S(m,n)}{n^m}, \quad m \ge n.
$$

A more convenient formula (no Stirling numbers) is the inclusion–exclusion form:

$$
P = \frac{1}{n^m} \sum_{j=0}^{n} (-1)^j \binom{n}{j} (n-j)^m.
$$

(For $m<n$ this probability is $0$.)

---

### Expected number of attempts to see all servers
The expected number of connection attempts to see every server at least once (the coupon-collector expectation) is

$$
\mathbb{E}[T] = n \sum_{k=1}^{n} \frac{1}{k} = n H_n,
$$

where $H_n$ is the $n$-th harmonic number.  
For large $n$, $\mathbb{E}[T] \approx n(\ln n + \gamma)$ where $\gamma$ is Euler’s constant.

---

### Example (numerical)
- For $n=5$, $m=10$: $P \approx 0.5225$ (52.25%).  
- For $n=5$, $m=7$: $P \approx 0.2150$ (21.50%).
- For $n=10$, $m=20$: $P \approx 0.2147$ (21.47%).

---

### If servers are **not** equally likely
If server $i$ has selection probability $p_i$ (with $\sum_i p_i=1$), the inclusion–exclusion generalizes to

$$
P(\text{all seen after } m) = \sum_{J \subseteq \{1..n\}} (-1)^{|J|} \Bigl( \sum_{i \notin J} p_i \Bigr)^{m}.
$$

---

**Assistant:**  
With $n=3$ and equal probabilities $p_i = \tfrac{1}{3}$, the probability that after $m$ random independent attempts you have seen all 3 servers is

$$
P(m) = 1 - 3 \left(\tfrac{2}{3}\right)^m + 3 \left(\tfrac{1}{3}\right)^m.
$$

Compute a few values:
- $P(10) \approx 0.9480$ (94.80%),  
- $P(11) \approx 0.9653$ (96.53%).

The smallest integer $m$ with $P(m) > 0.95$ is

$$
m = 11.
$$

(For reference: the expected number of attempts to see all 3 is $\approx 5.5$, but you need more tries to reach a 95% confidence.)

---

### Probability Table for $n=3$, uniform distribution

| m  | $P(m)$   | Percent |
|----:|---------:|--------:|
| 2  | 0.0000   | 0.00%   |
| 3  | 0.2222   | 22.22%  |
| 4  | 0.4444   | 44.44%  |
| 5  | 0.6173   | 61.73%  |
| 6  | 0.7407   | 74.07%  |
| 7  | 0.8260   | 82.60%  |
| 8  | 0.8848   | 88.48%  |
| 9  | 0.9249   | 92.49%  |
| 10 | 0.9480   | 94.80%  |
| 11 | 0.9653   | 96.53%  |
| 12 | 0.9769   | 97.69%  |
| 13 | 0.9850   | 98.50%  |
| 14 | 0.9902   | 99.02%  |
| 15 | 0.9938   | 99.38%  |

---


**Assistant:**  
$n=2$ (uniform load-balancer, each server chosen with probability $1/2$) with $P(m) > 95\%$ the math is simpler.

#### Formula
The probability that **both** servers have been seen at least once after $m$ independent attempts is

$$
P(m) = 1 - 2^{\,1-m}.
$$

(Valid for integer $m \ge 1$.)

#### Smallest $m$ with $P(m) > 95\%$
Solve $1 - 2^{1-m} > 0.95 \quad \Rightarrow \quad 2^{1-m} < 0.05$.  
That gives $m > 1 - \log_2(0.05) \approx 5.322$.  
So the smallest integer $m$ is

$$
m = 6.
$$

At $m=5$, $P=0.9375$ (93.75%); at $m=6$, $P=0.96875$ (96.875%).

#### Expected number of attempts
Coupon-collector expectation for $n=2$:

$$
\mathbb{E}[T] = 2 \left(1 + \tfrac{1}{2}\right) = 3.
$$

### Probability table for $n=2$, uniform distribution

| m | $P(m)$     | Percent |
|---:|-----------:|--------:|
| 1 | 0.00000000 | 0.00%   |
| 2 | 0.50000000 | 50.00%  |
| 3 | 0.75000000 | 75.00%  |
| 4 | 0.87500000 | 87.50%  |
| 5 | 0.93750000 | 93.75%  |
| 6 | 0.96875000 | 96.88%  |
| 7 | 0.98437500 | 98.44%  |
| 8 | 0.99218750 | 99.22%  |
| 9 | 0.99609375 | 99.61%  |
|10 | 0.99804688 | 99.80%  |

---
