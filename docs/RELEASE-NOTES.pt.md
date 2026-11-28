Julia's Launcher 0.3.18: menos crashes nas placas de 8 GB.

- Placas com 8 GB ou menos (e PCs com 16 GB de RAM) podiam crashar ao fim de poucos minutos: continuavam a ser criadas texturas novas quando a placa já tinha passado a memória que o Windows lhe dá, porque o espaço vazio dentro de blocos de texturas meio vazios contava como espaço livre. O excesso ia para a memória do PC até essa acabar também (erros 0x8007000E e depois 0x887A0005). Agora nenhuma textura nova é criada enquanto a placa estiver acima do orçamento; esperam até haver espaço.
- O resto da 0.3.17 fica igual.
