#include <stdio.h>
#include <stdint.h>

uint64_t echo_test(int32_t val){printf("The sum is %d\n", val);}
int32_t main() {
    uint8_t t_0[8];
    uint64_t t_1 = 0;
    uint64_t t_2 = (uint64_t)t_0;
    uint64_t t_3 = *(uint64_t*)t_2;
    uint64_t t_4 = (uint64_t)t_0;
    *(uint64_t*)t_4 = t_1;
    uint32_t t_5 = 114514;
    uint64_t t_6 = (uint64_t)t_0;
    uint64_t t_7 = *(uint64_t*)t_6;
    uint64_t t_8 = (uint64_t)t_0;
    *(uint32_t*)t_8 = t_5;
    int32_t t_9 = 10;
    int32_t t_10 = 20;
    uint8_t t_11[100];
    int32_t t_20 = f_0(t_9);
    int32_t t_21 = t_9 + t_20;
    uint64_t t_22 = echo_test(t_21);
    int32_t t_23 = 0;
    return t_23;
}
int32_t f_0(int32_t t_12) {
    int32_t t_13 = 0;
    int32_t t_14 = 1;
    int8_t t_15 = t_12 == t_13;
    int32_t t_16 = t_12 - t_14;
    int32_t t_18;
    if (t_15) {
        t_18 = t_13;
    } else {
        int32_t t_17 = f_0(t_16);
        t_18 = t_17;
    }
    int32_t t_19 = t_18 + t_12;
    return t_19;
}