# Test Questions for Municipality Chatbot

> Use these questions to test if the system routes correctly and provides accurate answers.

## Questions That Should Route to FAQ

### Working Hours (ساعات العمل)
| Question (AR) | Question (EN) | Expected Answer |
|---------------|---------------|-----------------|
| ما هي ساعات عمل البلدية؟ | What are the municipality working hours? | من السبت إلى الخميس، من 8:00 صباحاً حتى 2:00 ظهراً |
| متى تفتح البلدية؟ | When does the municipality open? | Same as above |
| شو أوقات الدوام؟ | What are the office hours? | Same as above |

### App Registration (التسجيل في التطبيق)
| Question (AR) | Question (EN) | Expected Answer |
|---------------|---------------|-----------------|
| كيف أسجل بالتطبيق؟ | How do I register in the app? | وضع رقم الهوية ورقم الجوال والانتظار لإرسال كلمة السر |
| كيفية التسجيل في التطبيق الإلكتروني؟ | How to register in the mobile app? | Same as above |

### Talk to Employee (محادثة مع موظف)
| Question (AR) | Question (EN) | Expected Answer |
|---------------|---------------|-----------------|
| أريد أتكلم مع موظف | I want to talk to an employee | خلال أوقات الدوام من 8:00 صباحاً حتى 2:00 ظهراً |
| كيف أتواصل مع خدمة العملاء؟ | How do I contact customer service? | Same as above |

---

## Questions That Should Route to API

### Pharmacies On Duty (صيدليات المناوبة)
| Question (AR) | Question (EN) | Expected Route | Expected Behavior |
|---------------|---------------|----------------|-------------------|
| ما هي صيدليات المناوبة اليوم؟ | Which pharmacies are on duty today? | API | Calls Pharmacies API, returns list |
| وين في صيدلية مفتوحة؟ | Where is an open pharmacy? | API | Same |
| أحتاج صيدلية طوارئ | I need an emergency pharmacy | API | Same |

### Water Schedule (جدول المياه)
| Question (AR) | Question (EN) | Expected Route | Expected Behavior |
|---------------|---------------|----------------|-------------------|
| متى موعد نزول المياه؟ | When is the water coming? | API | Calls Water Schedule API |
| شو جدول المياه اليوم؟ | What's the water schedule today? | API | Same |
| متى يجي الماء لمنطقتي؟ | When does water come to my area? | API | Same |

### Submit Complaint (تقديم شكوى)

**Flow: User initiates → Bot asks for details → User provides details → Bot submits**

#### Step 1: User Initiates Complaint
| Question (AR) | Question (EN) | Expected Route | Expected Response |
|---------------|---------------|----------------|-------------------|
| أريد تقديم شكوى | I want to submit a complaint | API | Follow-up question asking for details |
| أريد أشتكي | I want to complain | API | Same |
| عندي مشكلة | I have a problem | API | Same |

**Expected Follow-up Question (AR):**
> ما هي تفاصيل شكواك؟ يرجى ذكر: نوع المشكلة، الموقع، ورقم جوالك للتواصل

**Expected Follow-up Question (EN):**
> What are the details of your complaint? Please provide: type of problem, location, and your mobile number for contact.

#### Step 2: User Provides Details
| Question (AR) | Question (EN) | Expected Route | Expected Response |
|---------------|---------------|----------------|-------------------|
| عندي مشكلة صرف صحي في شارع الملك فيصل، رقمي 0599123456 | I have a sewage problem on King Faisal Street, my number is 0599123456 | API | Submits complaint, returns confirmation with ID |
| في تراكم نفايات قرب مسجد النور، جوالي 0598765432 | There's garbage near Al-Noor Mosque, my mobile is 0598765432 | API | Same |
| انقطاع مياه في حي الزيتون منذ يومين، 0597654321 | Water outage in Al-Zaytoun neighborhood for 2 days, 0597654321 | API | Same |

**Expected Confirmation Response (AR):**
> تم تقديم شكواك بنجاح. رقم الشكوى الخاص بك هو [XXXXXX]. سيتم التواصل معك قريباً.

**Expected Confirmation Response (EN):**
> Your complaint has been submitted successfully. Your complaint number is [XXXXXX]. We will contact you soon.

#### Complaint Categories (CATEGORY_SUB_ID)
| ID | Category (AR) | Category (EN) |
|----|---------------|---------------|
| 1 | نفايات | Garbage/Waste |
| 2 | صرف صحي | Sewage |
| 3 | مياه | Water |
| 4 | إنارة | Lighting |
| 5 | طرق | Roads |

---

## Questions Without API Support (Should Route to GENERAL or RAG)

These questions are in the original FAQ document but we DON'T have APIs for them:

| Question (AR) | Question (EN) | Expected Route | Notes |
|---------------|---------------|----------------|-------|
| أريد أستفسر عن طلب رقم 12345 | I want to check request #12345 | GENERAL | No request lookup API |
| ما هي فاتورة المياه؟ | What's my water bill? | GENERAL | No water bill API |
| أريد طلب تنك مياه | I want to request a water tank | GENERAL | No water tank API |
| ما هي الأقساط المتبقية؟ | What are my remaining installments? | GENERAL | No installments API |
| في انقطاع كهربا؟ | Is there a power outage? | GENERAL | No power outage API |

---

## Test Scenarios

### Scenario 1: Simple FAQ
```
User: ما هي ساعات عمل البلدية؟
Expected Route: FAQ
Expected Answer: أوقات الدوام الرسمية في بلدية الخليل هي: من السبت إلى الخميس، من الساعة 8:00 صباحاً حتى 2:00 ظهراً.
```

### Scenario 2: API Call - Pharmacies
```
User: وين في صيدلية مفتوحة؟
Expected Route: API
Expected Behavior: System calls http://egate.hebron-city.ps:8282/ai/pharmacies_on_duty
Expected Answer: List of pharmacies with names and addresses
```

### Scenario 3: API Call - Water Schedule
```
User: متى يجي الماء؟
Expected Route: API
Expected Behavior: System calls http://egate.hebron-city.ps:8282/api/WaterAPIController/Water_s_plan
Expected Answer: Water distribution schedule with areas and dates
```

### Scenario 4: Complaint Submission
```
User: أريد تقديم شكوى عن تراكم نفايات
Expected Route: API
Expected Behavior:
1. System asks for details (phone, location, description)
2. User provides info
3. System calls complaint API
4. Returns confirmation with complaint ID
```

### Scenario 5: Unsupported Query
```
User: ما هي فاتورة المياه الخاصة بي؟
Expected Route: GENERAL
Expected Answer: Should explain that this service requires visiting the office or using the mobile app (since we don't have the API)
```

### Scenario 6: Greeting
```
User: مرحبا
Expected Route: GENERAL
Expected Answer: Greeting response with available services
```

---

## Questions That Should Route to RAG (Document-Based)

These questions should be answered from the uploaded document `__ملحق طلبات للردا لالي_.docx`:

### Water Services (خدمات المياه)
| Question (AR) | Question (EN) | Expected Answer Contains |
|---------------|---------------|-------------------------|
| ما هي متطلبات طلب اشتراك مياه جديد؟ | What are the requirements for a new water subscription? | صورة هوية، إثبات ملكية، رسم موقع، كروكي |
| كيف أنقل اشتراك مياه باسمي؟ | How do I transfer a water subscription to my name? | صورة هوية، إثبات ملكية، تنازل من المالك السابق |
| ما هي إجراءات وقف اشتراك المياه؟ | What's the procedure to stop water subscription? | طلب خطي، صورة هوية |
| كيف أطلب تنك مياه؟ | How do I request a water tank? | مراجعة قسم المياه، تعبئة طلب |
| ما هي متطلبات فحص عداد المياه؟ | What are the requirements for water meter inspection? | طلب خطي، صورة هوية، رقم الاشتراك |

### Electricity Services (خدمات الكهرباء)
| Question (AR) | Question (EN) | Expected Answer Contains |
|---------------|---------------|-------------------------|
| كيف أحصل على اشتراك كهرباء جديد؟ | How do I get a new electricity subscription? | صورة هوية، إثبات ملكية، فحص كهربائي |
| ما هي متطلبات نقل اشتراك الكهرباء؟ | What are the requirements to transfer electricity subscription? | صورة هوية، تنازل، إثبات ملكية |
| كيف أطلب زيادة أمبير الكهرباء؟ | How do I request an amperage increase? | طلب خطي، فحص كهربائي |

### Sewage Services (خدمات الصرف الصحي)
| Question (AR) | Question (EN) | Expected Answer Contains |
|---------------|---------------|-------------------------|
| كيف أحصل على إيصال صرف صحي؟ | How do I get a sewage connection? | طلب، صورة هوية، رسم موقع |
| ما هي إجراءات شفط بئر الامتصاص؟ | What's the procedure for septic tank pumping? | مراجعة القسم، تعبئة طلب |

### Building Services (خدمات الأبنية)
| Question (AR) | Question (EN) | Expected Answer Contains |
|---------------|---------------|-------------------------|
| ما هي متطلبات رخصة البناء؟ | What are building permit requirements? | مخططات هندسية، إثبات ملكية، موافقات |
| كيف أحصل على رخصة إضافة طابق؟ | How do I get a permit to add a floor? | مخططات، موافقة مهندس، رخصة أصلية |
| ما هي متطلبات رخصة الترميم؟ | What are renovation permit requirements? | طلب، صورة هوية، وصف الأعمال |
| كيف أحصل على شهادة إتمام بناء؟ | How do I get a building completion certificate? | رخصة البناء، فحص ميداني |

### Planning & Surveying (التخطيط والمساحة)
| Question (AR) | Question (EN) | Expected Answer Contains |
|---------------|---------------|-------------------------|
| كيف أطلب مخطط موقع؟ | How do I request a site plan? | طلب، صورة هوية، إثبات ملكية |
| ما هي متطلبات طلب إفراز أرض؟ | What are land subdivision requirements? | سند ملكية، مخطط مساحي |

### Health & Environment (الصحة والبيئة)
| Question (AR) | Question (EN) | Expected Answer Contains |
|---------------|---------------|-------------------------|
| كيف أحصل على رخصة مهنة صحية؟ | How do I get a health profession license? | شهادة صحية، صورة هوية |
| ما هي متطلبات رخصة محل تجاري؟ | What are commercial shop license requirements? | عقد إيجار، صورة هوية، موافقات |

### Public Services (الخدمات العامة)
| Question (AR) | Question (EN) | Expected Answer Contains |
|---------------|---------------|-------------------------|
| كيف أطلب صيانة شارع؟ | How do I request road maintenance? | طلب خطي، وصف المشكلة |
| كيف أبلغ عن مشكلة إنارة؟ | How do I report a lighting problem? | الاتصال أو تقديم شكوى |

### Crafts & Industry Licenses (رخص الحرف والصناعات)
| Question (AR) | Question (EN) | Expected Answer Contains |
|---------------|---------------|-------------------------|
| ما هي متطلبات رخصة ورشة؟ | What are workshop license requirements? | صورة هوية، عقد إيجار، موافقة بيئية |
| كيف أجدد رخصة المهنة؟ | How do I renew a profession license? | الرخصة السابقة، صورة هوية |

---

## RAG Test Scenarios

### Scenario 7: RAG - Water Subscription
```
User: شو بدي أجيب معي عشان أسجل اشتراك مياه جديد؟
Expected Route: RAG
Expected Answer: Should mention: صورة هوية، إثبات ملكية، رسم موقع/كروكي
Source: __ملحق طلبات للردا لالي_.docx
```

### Scenario 8: RAG - Building Permit
```
User: بدي أبني طابق جديد، شو المطلوب؟
Expected Route: RAG
Expected Answer: Should mention: مخططات هندسية، رخصة البناء الأصلية، موافقة مهندس
Source: __ملحق طلبات للردا لالي_.docx
```

### Scenario 9: RAG - Electricity Subscription
```
User: كيف أحصل على عداد كهرباء لبيتي الجديد؟
Expected Route: RAG
Expected Answer: Should mention: صورة هوية، إثبات ملكية، فحص كهربائي معتمد
Source: __ملحق طلبات للردا لالي_.docx
```

### Scenario 10: RAG - Commercial License
```
User: بدي أفتح محل، شو الأوراق المطلوبة؟
Expected Route: RAG
Expected Answer: Should mention: صورة هوية، عقد إيجار، شهادة صحية، موافقات
Source: __ملحق طلبات للردا لالي_.docx
```

---

## Verification Checklist

### FAQ Tests
- [ ] FAQ questions return exact pre-written answers
- [ ] Arabic FAQ questions return Arabic answers
- [ ] English FAQ questions return English answers

### API Tests
- [ ] Pharmacy questions trigger API call and return real data
- [ ] Water schedule questions trigger API call and return real data
- [ ] Complaint submission collects info then calls API

### RAG Tests (Requires docx upload first)
- [ ] Water subscription questions return document-based answers
- [ ] Building permit questions return document-based answers
- [ ] Electricity subscription questions return document-based answers
- [ ] Commercial license questions return document-based answers

### General Tests
- [ ] Unsupported queries get helpful GENERAL responses
- [ ] Arabic and English both work correctly
- [ ] Greetings are handled appropriately
- [ ] Language of response matches language of question
